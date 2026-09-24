/*
 * Endcord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { Settings, SettingsStore } from "@api/Settings";

const STYLE_ID = "endcord-custom-background-style";
const ROOT_ID = "endcord-custom-background";

const STYLE = `
#${ROOT_ID} {
    position: fixed;
    inset: 0;
    z-index: 0;
    pointer-events: none;
    overflow: hidden;
    background: #000;
}
#${ROOT_ID} img,
#${ROOT_ID} video {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: var(--ec-bg-fit, cover);
    opacity: var(--ec-bg-opacity, 1);
    filter: blur(var(--ec-bg-blur, 0px));
    transform: scale(1.06);
}
#${ROOT_ID} .ec-bg-dim {
    position: absolute;
    inset: 0;
    background: #000;
    opacity: var(--ec-bg-dim, 0);
}
html.endcord-has-custom-bg,
html.endcord-has-custom-bg body,
html.endcord-has-custom-bg #app-mount {
    background: transparent !important;
}
html.endcord-has-custom-bg #app-mount {
    position: relative;
    z-index: 1;
}
html.endcord-has-custom-bg.theme-dark {
    --background-base-lowest: rgba(0, 0, 0, var(--ec-panel, 0.72)) !important;
    --background-base-lower: rgba(0, 0, 0, var(--ec-panel, 0.72)) !important;
    --background-base-low: rgba(8, 8, 8, var(--ec-panel, 0.78)) !important;
    --background-primary: rgba(0, 0, 0, var(--ec-panel, 0.72)) !important;
    --background-secondary: rgba(0, 0, 0, var(--ec-panel, 0.65)) !important;
    --background-secondary-alt: rgba(0, 0, 0, var(--ec-panel, 0.65)) !important;
    --background-tertiary: rgba(0, 0, 0, var(--ec-panel, 0.55)) !important;
    --bg-overlay-app-frame: transparent !important;
    --bg-overlay-chat: rgba(0, 0, 0, var(--ec-panel, 0.55)) !important;
}
html.endcord-has-custom-bg.theme-light {
    --background-base-lowest: rgba(255, 255, 255, var(--ec-panel, 0.72)) !important;
    --background-base-lower: rgba(255, 255, 255, var(--ec-panel, 0.72)) !important;
    --background-base-low: rgba(248, 248, 248, var(--ec-panel, 0.78)) !important;
    --background-primary: rgba(255, 255, 255, var(--ec-panel, 0.72)) !important;
    --background-secondary: rgba(255, 255, 255, var(--ec-panel, 0.65)) !important;
    --background-secondary-alt: rgba(255, 255, 255, var(--ec-panel, 0.65)) !important;
    --background-tertiary: rgba(255, 255, 255, var(--ec-panel, 0.55)) !important;
    --bg-overlay-app-frame: transparent !important;
    --bg-overlay-chat: rgba(255, 255, 255, var(--ec-panel, 0.55)) !important;
}
`;

export function backgroundUrl(fileName: string) {
    return `endcord:///backgrounds/${encodeURIComponent(fileName)}?v=${Date.now()}`;
}

export function kindFromName(fileName: string): "image" | "video" | "" {
    const ext = fileName.split(".").pop()?.toLowerCase() ?? "";
    if (["mp4", "webm", "mov"].includes(ext)) return "video";
    if (["png", "jpg", "jpeg", "gif", "webp"].includes(ext)) return "image";
    return "";
}

function ensureStyle() {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = STYLE;
    document.documentElement.appendChild(style);
}

export function applyCustomBackground() {
    if (typeof document === "undefined") return;
    ensureStyle();

    const bg = Settings.customBackground;
    const active = !!bg?.enabled && !!bg.fileName;
    document.documentElement.classList.toggle("endcord-has-custom-bg", active);
    document.documentElement.style.setProperty("--ec-bg-opacity", String((bg?.opacity ?? 100) / 100));
    document.documentElement.style.setProperty("--ec-bg-blur", `${bg?.blur ?? 0}px`);
    document.documentElement.style.setProperty("--ec-bg-dim", String((bg?.dim ?? 0) / 100));
    document.documentElement.style.setProperty("--ec-panel", String((bg?.panel ?? 72) / 100));
    document.documentElement.style.setProperty("--ec-bg-fit", bg?.fit || "cover");

    let root = document.getElementById(ROOT_ID);
    if (!active) {
        root?.remove();
        return;
    }

    if (!root) {
        root = document.createElement("div");
        root.id = ROOT_ID;
        document.body.prepend(root);
    }

    if (root.dataset.file !== bg.fileName || root.dataset.kind !== bg.kind) {
        const src = backgroundUrl(bg.fileName);
        root.dataset.file = bg.fileName;
        root.dataset.kind = bg.kind;
        root.replaceChildren();
        if (bg.kind === "video") {
            const video = document.createElement("video");
            video.src = src;
            video.autoplay = true;
            video.muted = true;
            video.loop = bg.loop !== false;
            video.playsInline = true;
            root.appendChild(video);
        } else {
            const img = document.createElement("img");
            img.src = src;
            img.alt = "";
            root.appendChild(img);
        }
        const dim = document.createElement("div");
        dim.className = "ec-bg-dim";
        root.appendChild(dim);
    } else {
        const video = root.querySelector("video");
        if (video) video.loop = bg.loop !== false;
    }
}

export function initCustomBackground() {
    applyCustomBackground();
    SettingsStore.addPrefixChangeListener("customBackground", applyCustomBackground);
}
