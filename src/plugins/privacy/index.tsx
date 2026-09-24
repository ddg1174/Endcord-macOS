/*
 * Endcord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { MessageObject } from "@api/MessageEvents";
import { definePluginSettings, PlainSettings, Settings } from "@api/Settings";
import { getUserSettingLazy } from "@api/UserSettings";
import { Devs } from "@utils/constants";
import { Logger } from "@utils/Logger";
import definePlugin, { OptionType } from "@utils/types";
import { search } from "@webpack";
import { RestAPI } from "@webpack/common";

const logger = new Logger("Privacy", "#a6d189");

const TRACKING_PARAM = /^(?:utm_.+|fbclid|gclid|gbraid|wbraid|dclid|msclkid|twclid|ttclid|igshid|igsh|mc_eid|mc_cid|_hsenc|_hsmi|mkt_tok|yclid|vero_id|vero_conv|oly_anon_id|oly_enc_id|fb_action_ids|fb_action_types|fb_source|sc_cid|wickedid|rb_clickid|epik)$/i;
const TELEMETRY_URL = /\/science(?:[/?#]|$)|\/metrics(?:[/?#]|$)|ingest\.sentry\.io|error-reporting-proxy|\/error-reporting(?:[/?#]|$)|\/users\/@me\/affinities(?:[/?#]|$)/i;
const URL_IN_TEXT = /https?:\/\/[^\s<]+[^<.,:;"'>)|\]\s]/g;
const ACK_POST = /([\w.]+)\.post\((\{(?:[^{}]|\{[^{}]*\})*\})\)/g;

const StatusSettings = getUserSettingLazy<string>("status", "status")!;
const ShowCurrentGame = getUserSettingLazy<boolean>("status", "showCurrentGame")!;

let ackPatchDetected = false;
let ackFallbackInstalled = false;
let originalPost: ((opts: any, ...rest: any[]) => any) | null = null;
let presenceReady = false;

const settings = definePluginSettings({
    privacyMode: {
        type: OptionType.BOOLEAN,
        description: "一次套用下面的保護。關掉後會還原。",
        default: true,
        onChange: applyPrivacy
    },
    hideTyping: {
        type: OptionType.BOOLEAN,
        description: "別人看不到你正在輸入。",
        default: true,
        onChange: applyPrivacy
    },
    hideReadReceipts: {
        type: OptionType.BOOLEAN,
        description: "不把已讀送到伺服器。你自己的未讀仍會清掉。",
        default: true,
        onChange: applyPrivacy
    },
    hidePresence: {
        type: OptionType.BOOLEAN,
        description: "朋友會看到你離線。關掉後回到原本的狀態。",
        default: true,
        onChange: applyPrivacy
    },
    stripLinks: {
        type: OptionType.BOOLEAN,
        description: "送出與編輯訊息時去掉廣告與社群平台的追蹤參數。",
        default: true,
        onChange: applyPrivacy
    },
    blockTelemetry: {
        type: OptionType.BOOLEAN,
        description: "擋下 Discord 的分析、指標、當機回報，以及好友推薦用的 affinities。",
        default: true,
        onChange: applyPrivacy
    },
    hideActivity: {
        type: OptionType.BOOLEAN,
        description: "不分享正在玩的遊戲、Spotify 和自訂動態。",
        default: true,
        onChange: applyPrivacy
    },
    typingSnapshotTaken: {
        type: OptionType.BOOLEAN,
        description: "internal",
        default: false,
        hidden: true
    },
    typingWasEnabled: {
        type: OptionType.BOOLEAN,
        description: "internal",
        default: false,
        hidden: true
    },
    typingWasActive: {
        type: OptionType.BOOLEAN,
        description: "internal",
        default: true,
        hidden: true
    },
    previousStatus: {
        type: OptionType.STRING,
        description: "internal",
        default: "",
        hidden: true
    },
    activitySnapshotTaken: {
        type: OptionType.BOOLEAN,
        description: "internal",
        default: false,
        hidden: true
    },
    activityWasShown: {
        type: OptionType.BOOLEAN,
        description: "internal",
        default: true,
        hidden: true
    }
});

function privacyOn() {
    return !!settings.store.privacyMode;
}

function isAckUrl(url: unknown) {
    if (typeof url !== "string") return false;
    return /\/messages\/[^/?#]+\/ack(?:[/?#]|$)/.test(url)
        || url.includes("/read-states/ack-bulk")
        || /\/ack-bulk(?:[/?#]|$)/.test(url);
}

function shouldBlockAck(url: unknown) {
    return privacyOn() && !!settings.store.hideReadReceipts && isAckUrl(url);
}

function requestUrl(input: unknown) {
    if (typeof input === "string") return input;
    if (input instanceof URL) return input.href;
    if (input && typeof input === "object" && "url" in input && typeof (input as { url?: unknown; }).url === "string")
        return (input as { url: string; }).url;
    return "";
}

function shouldBlockTelemetry(url: unknown) {
    return privacyOn() && !!settings.store.blockTelemetry && typeof url === "string" && TELEMETRY_URL.test(url);
}

function shouldDropRequest(url: unknown) {
    return shouldBlockAck(url) || shouldBlockTelemetry(url);
}

function blockedAckResponse() {
    return Promise.resolve({ ok: true, status: 204, body: {}, headers: {} });
}

function detectAckPatch() {
    if (ackPatchDetected) return;
    try {
        const modules = search(/MESSAGE_ACK|\/ack|ack-bulk/);
        ackPatchDetected = Object.values(modules).some(factory => {
            ACK_POST.lastIndex = 0;
            return ACK_POST.test(Function.prototype.toString.call(factory));
        });
    } catch (e) {
        logger.error("Failed to scan for read receipt send", e);
    }
}

const restOriginals = new Map<string, (...args: any[]) => any>();
let fetchInstalled = false;
let originalFetch: typeof fetch | null = null;
let xhrInstalled = false;
let originalXhrOpen: typeof XMLHttpRequest.prototype.open | null = null;
let originalXhrSend: typeof XMLHttpRequest.prototype.send | null = null;

function installAckFallback() {
    if (ackFallbackInstalled) return;
    const api = RestAPI as any;
    for (const method of ["get", "post", "put", "patch", "del", "delete", "request"]) {
        if (typeof api?.[method] !== "function" || restOriginals.has(method)) continue;
        const original = api[method].bind(api);
        restOriginals.set(method, original);
        api[method] = (opts: any, ...rest: any[]) => {
            if (shouldDropRequest(opts?.url)) return blockedAckResponse();
            return original(opts, ...rest);
        };
    }
    if (typeof api?.post === "function") {
        originalPost = restOriginals.get("post") ?? null;
        ackFallbackInstalled = true;
    }
    installFetchHook();
    installXhrHook();
}

function restoreAckFallback() {
    const api = RestAPI as any;
    for (const [method, original] of restOriginals)
        api[method] = original;
    restOriginals.clear();
    originalPost = null;
    ackFallbackInstalled = false;
    restoreFetchHook();
    restoreXhrHook();
}

function installFetchHook() {
    if (fetchInstalled || typeof window.fetch !== "function") return;
    originalFetch = window.fetch.bind(window);
    window.fetch = ((input: RequestInfo | URL, init?: RequestInit) => {
        if (shouldDropRequest(requestUrl(input)))
            return Promise.resolve(new Response(null, { status: 204 }));
        return originalFetch!(input, init);
    }) as typeof fetch;
    fetchInstalled = true;
}

function restoreFetchHook() {
    if (!fetchInstalled || !originalFetch) return;
    window.fetch = originalFetch;
    originalFetch = null;
    fetchInstalled = false;
}

function installXhrHook() {
    if (xhrInstalled) return;
    originalXhrOpen = XMLHttpRequest.prototype.open;
    originalXhrSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (this: XMLHttpRequest & { __endcordUrl?: string; }, method: string, url: string | URL, ...rest: any[]) {
        this.__endcordUrl = String(url);
        return (originalXhrOpen as any).call(this, method, url, ...rest);
    };
    XMLHttpRequest.prototype.send = function (this: XMLHttpRequest & { __endcordUrl?: string; }, body?: Document | XMLHttpRequestBodyInit | null) {
        if (shouldDropRequest(this.__endcordUrl)) {
            setTimeout(() => {
                this.dispatchEvent(new Event("load"));
                this.dispatchEvent(new Event("loadend"));
            }, 0);
            return;
        }
        return originalXhrSend!.call(this, body);
    };
    xhrInstalled = true;
}

function restoreXhrHook() {
    if (!xhrInstalled) return;
    if (originalXhrOpen) XMLHttpRequest.prototype.open = originalXhrOpen;
    if (originalXhrSend) XMLHttpRequest.prototype.send = originalXhrSend;
    originalXhrOpen = null;
    originalXhrSend = null;
    xhrInstalled = false;
}

export function getReadReceiptStatus(): "patched" | "fallback" | "missing" {
    detectAckPatch();
    if (ackPatchDetected) return "patched";
    if (ackFallbackInstalled) return "fallback";
    return "missing";
}

export function isPresenceReady() {
    return presenceReady;
}

export function isTelemetryHooked() {
    return fetchInstalled || restOriginals.size > 0 || xhrInstalled;
}

function applyTyping(hide: boolean) {
    const typing = Settings.plugins.SilentTyping;
    if (!typing) return;

    if (hide) {
        typing.enabled = true;
        typing.isEnabled = true;
        return;
    }

    if (settings.store.typingWasEnabled) {
        typing.enabled = true;
        typing.isEnabled = settings.store.typingWasActive !== false;
        return;
    }

    typing.enabled = false;
    typing.isEnabled = false;
}

function applyPresence(hide: boolean) {
    try {
        const current = StatusSettings.getSetting();
        if (typeof current !== "string") {
            presenceReady = false;
            return;
        }
        presenceReady = true;

        if (hide) {
            if (!settings.store.previousStatus)
                settings.store.previousStatus = current;
            if (current !== "invisible")
                void StatusSettings.updateSetting("invisible");
            return;
        }

        const previous = settings.store.previousStatus;
        if (!previous) return;
        settings.store.previousStatus = "";
        if (previous !== current)
            void StatusSettings.updateSetting(previous);
    } catch (e) {
        presenceReady = false;
        logger.error("Failed to update online status", e);
    }
}

function stripTracking(content: string) {
    if (!/https?:\/\//.test(content)) return content;
    return content.replace(URL_IN_TEXT, raw => {
        try {
            const url = new URL(raw);
            let changed = false;
            for (const key of [...url.searchParams.keys()]) {
                if (!TRACKING_PARAM.test(key)) continue;
                url.searchParams.delete(key);
                changed = true;
            }
            return changed ? url.toString() : raw;
        } catch {
            return raw;
        }
    });
}

function cleanMessage(msg: MessageObject) {
    if (!(privacyOn() && settings.store.stripLinks)) return;
    if (typeof msg?.content === "string")
        msg.content = stripTracking(msg.content);
}

export function captureTypingSnapshot() {
    const plugins = PlainSettings.plugins as Record<string, any>;
    const privacy = (plugins.Privacy ??= { enabled: true });
    if (privacy.enabled == null) privacy.enabled = true;
    if (privacy.typingSnapshotTaken) return;

    const typing = plugins.SilentTyping as { enabled?: boolean; isEnabled?: boolean; } | undefined;
    privacy.typingSnapshotTaken = true;
    privacy.typingWasEnabled = !!typing?.enabled;
    privacy.typingWasActive = typing?.isEnabled !== false;
}

function captureActivitySnapshot() {
    if (settings.store.activitySnapshotTaken) return;
    try {
        const current = ShowCurrentGame.getSetting();
        if (typeof current !== "boolean") return;
        settings.store.activityWasShown = current;
        settings.store.activitySnapshotTaken = true;
    } catch (e) {
        logger.error("Failed to read game activity setting", e);
    }
}

function applyActivity(hide: boolean) {
    try {
        const current = ShowCurrentGame.getSetting();
        if (typeof current !== "boolean") return;
        if (!settings.store.activitySnapshotTaken) {
            settings.store.activityWasShown = current;
            settings.store.activitySnapshotTaken = true;
        }
        if (hide) {
            if (current !== false)
                void ShowCurrentGame.updateSetting(false);
            return;
        }
        if (settings.store.activityWasShown && current === false)
            void ShowCurrentGame.updateSetting(true);
    } catch (e) {
        logger.error("Failed to update game activity setting", e);
    }
}

function applyPrivacy() {
    const on = privacyOn();
    applyTyping(on && !!settings.store.hideTyping);
    applyPresence(on && !!settings.store.hidePresence);
    applyActivity(on && !!settings.store.hideActivity);
}

export function refreshPrivacyRuntime() {
    detectAckPatch();
    installAckFallback();
    applyPrivacy();
}

export { settings };

export default definePlugin({
    name: "Privacy",
    description: "擋下正在輸入、已讀、上線狀態、遊戲動態，以及 Discord 的分析與連結追蹤",
    authors: [Devs.Endcord],
    tags: ["Privacy"],
    enabledByDefault: true,
    dependencies: ["SilentTyping", "UserSettingsAPI"],
    settings,

    patches: [
        {
            find: /MESSAGE_ACK|\/ack|ack-bulk/,
            all: true,
            noWarn: true,
            replacement: {
                match: ACK_POST,
                replace: "$self.sendAck($1.post.bind($1),$2)",
                noWarn: true
            }
        }
    ],

    sendAck(post: (req: any) => any, req: { url?: string; }) {
        if (shouldBlockAck(req?.url)) return blockedAckResponse();
        return post(req);
    },

    onBeforeMessageSend(_channelId: string, msg: MessageObject) {
        cleanMessage(msg);
    },

    onBeforeMessageEdit(_channelId: string, _messageId: string, msg: MessageObject) {
        cleanMessage(msg);
    },

    start() {
        captureActivitySnapshot();
        refreshPrivacyRuntime();
    },

    stop() {
        restoreAckFallback();
        applyTyping(false);
        applyPresence(false);
        applyActivity(false);
    }
});
