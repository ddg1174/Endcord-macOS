/*
 * Endcord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { fetchBuffer, fetchJson } from "@main/utils/http";
import { ENDCORD_USER_AGENT } from "@shared/endcordUserAgent";
import { IpcEvents } from "@shared/IpcEvents";
import { ipcMain } from "electron";
import { readFileSync } from "fs";
import { rename, rm, writeFile } from "fs/promises";
import { join } from "path";

import gitHash from "~git-hash";
import gitRemote from "~git-remote";

import { serializeErrors } from "./common";

const API_BASE = `https://api.github.com/repos/${gitRemote}`;
const DIST_FILES = [
    "patcher.js",
    "patcher.js.map",
    "preload.js",
    "preload.js.map",
    "renderer.js",
    "renderer.js.map",
    "renderer.css",
    "renderer.css.map"
];

const githubHeaders = {
    Accept: "application/vnd.github+json",
    "User-Agent": ENDCORD_USER_AGENT
};

let pendingVersion: string | null = null;

function readLocalVersion() {
    try {
        const parsed = JSON.parse(readFileSync(join(__dirname, "version.json"), "utf8"));
        if (typeof parsed?.version === "string" && parsed.version)
            return parsed.version;
    } catch { }
    return gitHash;
}

async function githubGet<T = any>(endpoint: string) {
    return fetchJson<T>(API_BASE + endpoint, { headers: githubHeaders });
}

async function latestCommitSha() {
    const data = await githubGet<{ sha?: string }>("/commits/main");
    if (!data?.sha)
        throw new Error("GitHub 沒有回傳最新版本");
    return data.sha;
}

function rawUrl(sha: string, name: string) {
    return `https://raw.githubusercontent.com/${gitRemote}/${sha}/publish/dist/${name}`;
}

async function fetchRemoteVersion(sha: string) {
    const data = await fetchJson<{ version?: string }>(rawUrl(sha, "version.json"), {
        headers: {
            Accept: "application/json",
            "User-Agent": ENDCORD_USER_AGENT
        }
    });
    if (!data?.version)
        throw new Error("GitHub 上的 version.json 沒有版本號碼");
    return data.version;
}

function looksValid(name: string, body: Buffer) {
    if (body.length < 16) return false;
    const head = body.subarray(0, 120).toString("utf8").trimStart().toLowerCase();
    if (head.startsWith("<!") || head.startsWith("<html") || head.startsWith("not found"))
        return false;
    if (name === "version.json" || name.endsWith(".map"))
        return head.startsWith("{");
    if (name.endsWith(".js"))
        return body.length > 500 && body.includes(Buffer.from("Endcord"));
    return true;
}

async function calculateGitChanges() {
    const sha = await latestCommitSha();
    const remoteVersion = await fetchRemoteVersion(sha);
    if (remoteVersion === readLocalVersion()) {
        pendingVersion = null;
        return [];
    }

    pendingVersion = remoteVersion;

    try {
        const commits = await githubGet<any[]>(`/commits?sha=${sha}&per_page=8`);
        return commits.map(c => ({
            hash: String(c.sha ?? remoteVersion).slice(0, 7),
            author: c.author?.login ?? c.commit?.author?.name ?? "GitHub",
            message: String(c.commit?.message ?? "GitHub 上有新版本").split("\n")[0]
        }));
    } catch {
        return [{
            hash: remoteVersion,
            author: "GitHub",
            message: "GitHub 上有新版本"
        }];
    }
}

async function fetchUpdates() {
    if (pendingVersion) return true;
    const changes = await calculateGitChanges();
    return changes.length > 0;
}

async function applyUpdates() {
    const sha = await latestCommitSha();
    const remoteVersion = await fetchRemoteVersion(sha);
    if (remoteVersion === readLocalVersion()) {
        pendingVersion = null;
        return true;
    }

    const names = ["version.json", ...DIST_FILES];
    const staged: [string, string][] = [];

    try {
        for (const name of names) {
            let body: Buffer;
            try {
                body = await fetchBuffer(rawUrl(sha, name), { headers: githubHeaders });
            } catch (err) {
                if (name.endsWith(".map")) continue;
                throw err;
            }

            if (!looksValid(name, body))
                throw new Error("從 GitHub 下載的檔案不正確：" + name);

            const dest = join(__dirname, name);
            const tmp = dest + ".download";
            await writeFile(tmp, body);
            staged.push([tmp, dest]);
        }

        for (const [tmp, dest] of staged) {
            await rm(dest, { force: true });
            await rename(tmp, dest);
        }
    } catch (err) {
        await Promise.all(staged.map(([tmp]) => rm(tmp, { force: true })));
        throw err;
    }

    pendingVersion = null;
    return true;
}

ipcMain.handle(IpcEvents.GET_REPO, serializeErrors(() => `https://github.com/${gitRemote}`));
ipcMain.handle(IpcEvents.GET_UPDATES, serializeErrors(calculateGitChanges));
ipcMain.handle(IpcEvents.UPDATE, serializeErrors(fetchUpdates));
ipcMain.handle(IpcEvents.BUILD, serializeErrors(applyUpdates));
