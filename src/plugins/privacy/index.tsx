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

const TRACKING_PARAM = /^(?:utm_.+|fbclid|gclid|mc_eid|igshid)$/i;
const URL_IN_TEXT = /https?:\/\/[^\s<]+[^<.,:;"'>)|\]\s]/g;
const ACK_POST = /([\w.]+)\.post\((\{(?:[^{}]|\{[^{}]*\})*\})\)/g;

const StatusSettings = getUserSettingLazy<string>("status", "status")!;

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
        description: "送出與編輯訊息時去掉 utm、fbclid、gclid、mc_eid、igshid。",
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

function installAckFallback() {
    if (ackFallbackInstalled) return;
    const api = RestAPI as any;
    if (typeof api?.post !== "function") return;
    originalPost = api.post.bind(api);
    api.post = (opts: any, ...rest: any[]) => {
        if (shouldBlockAck(opts?.url)) return blockedAckResponse();
        return originalPost!(opts, ...rest);
    };
    ackFallbackInstalled = true;
}

function restoreAckFallback() {
    if (!ackFallbackInstalled || !originalPost) return;
    (RestAPI as any).post = originalPost;
    originalPost = null;
    ackFallbackInstalled = false;
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

function applyPrivacy() {
    const on = privacyOn();
    applyTyping(on && !!settings.store.hideTyping);
    applyPresence(on && !!settings.store.hidePresence);
}

export function refreshPrivacyRuntime() {
    detectAckPatch();
    installAckFallback();
    applyPrivacy();
}

export { settings };

export default definePlugin({
    name: "Privacy",
    description: "讓 Discord 少把你的正在輸入、已讀、上線狀態和連結追蹤送出去",
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
        refreshPrivacyRuntime();
    },

    stop() {
        restoreAckFallback();
        applyTyping(false);
        applyPresence(false);
    }
});
