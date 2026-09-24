/*
 * Endcord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { Settings, useSettings } from "@api/Settings";
import { backgroundUrl, kindFromName } from "@api/customBackground";
import { Card } from "@components/Card";
import { FormSwitch } from "@components/FormSwitch";
import { Forms, showToast, useRef } from "@webpack/common";

const MAX_BYTES = 80 * 1024 * 1024;

function RangeRow({ label, value, min, max, suffix, onChange }: {
    label: string;
    value: number;
    min: number;
    max: number;
    suffix: string;
    onChange: (value: number) => void;
}) {
    return (
        <div style={{ marginTop: "0.8em" }}>
            <Forms.FormTitle>{label}：{value}{suffix}</Forms.FormTitle>
            <input
                type="range"
                min={min}
                max={max}
                value={value}
                onChange={e => onChange(Number(e.currentTarget.value))}
                style={{ width: "100%" }}
            />
        </div>
    );
}

export function CustomBackgroundCard() {
    const settings = useSettings(["customBackground.*"]);
    const bg = settings.customBackground;
    const fileRef = useRef<HTMLInputElement>(null);

    async function onPick() {
        const file = fileRef.current?.files?.[0];
        if (!file) return;
        const kind = kindFromName(file.name);
        if (!kind) {
            showToast("請選擇圖片、GIF 或影片");
            return;
        }
        if (file.size > MAX_BYTES) {
            showToast("檔案不能超過 80 MB");
            return;
        }

        try {
            const data = new Uint8Array(await file.arrayBuffer());
            const fileName = await EndcordNative.themes.saveBackground(file.name, data);
            bg.fileName = fileName;
            bg.kind = kind;
            bg.enabled = true;
            showToast("背景已套用");
        } catch (error) {
            showToast("背景儲存失敗");
            console.error(error);
        } finally {
            if (fileRef.current) fileRef.current.value = "";
        }
    }

    async function clear() {
        await EndcordNative.themes.clearBackground().catch(() => { });
        bg.fileName = "";
        bg.kind = "";
        bg.enabled = false;
    }

    const preview = bg.fileName ? backgroundUrl(bg.fileName) : "";

    return (
        <Card>
            <Forms.FormTitle tag="h5">自訂背景</Forms.FormTitle>
            <Forms.FormText>上傳圖片、GIF 或影片，當成 Discord 的背景。影片會靜音循環播放。</Forms.FormText>

            <div style={{ display: "flex", gap: "0.6em", marginTop: "0.8em", flexWrap: "wrap" }}>
                <button type="button" className="vc-bg-upload" onClick={() => fileRef.current?.click()}>
                    上傳背景
                </button>
                {bg.fileName && (
                    <button type="button" className="vc-bg-upload" onClick={clear}>
                        移除背景
                    </button>
                )}
                <input
                    ref={fileRef}
                    type="file"
                    accept="image/png,image/jpeg,image/gif,image/webp,video/mp4,video/webm,video/quicktime,.mov"
                    style={{ display: "none" }}
                    onChange={onPick}
                />
            </div>

            {preview && bg.kind === "image" && (
                <img src={preview} alt="" style={{ marginTop: "0.8em", width: "100%", maxHeight: 160, objectFit: "cover", borderRadius: 8 }} />
            )}
            {preview && bg.kind === "video" && (
                <video src={preview} muted autoPlay loop playsInline style={{ marginTop: "0.8em", width: "100%", maxHeight: 160, objectFit: "cover", borderRadius: 8 }} />
            )}

            <FormSwitch
                title="使用這個背景"
                description="關掉後回到原本的 Discord 背景，檔案會留著。"
                value={bg.enabled}
                disabled={!bg.fileName}
                onChange={(value: boolean) => bg.enabled = value}
            />
            <FormSwitch
                title="影片循環播放"
                description="GIF 本來就會重複。這項只影響影片。"
                value={bg.loop}
                disabled={bg.kind !== "video"}
                onChange={(value: boolean) => bg.loop = value}
                hideBorder
            />

            <RangeRow label="背景透明度" value={bg.opacity} min={10} max={100} suffix="%" onChange={value => bg.opacity = value} />
            <RangeRow label="模糊" value={bg.blur} min={0} max={40} suffix="px" onChange={value => bg.blur = value} />
            <RangeRow label="壓暗" value={bg.dim} min={0} max={80} suffix="%" onChange={value => bg.dim = value} />
            <RangeRow label="介面不透明度" value={bg.panel} min={20} max={100} suffix="%" onChange={value => bg.panel = value} />

            <Forms.FormTitle style={{ marginTop: "0.8em" }}>縮放方式</Forms.FormTitle>
            <select
                value={bg.fit}
                onChange={e => Settings.customBackground.fit = e.currentTarget.value as typeof bg.fit}
                style={{ width: "100%", marginTop: "0.3em" }}
            >
                <option value="cover">填滿畫面</option>
                <option value="contain">完整顯示</option>
                <option value="fill">拉伸</option>
            </select>
        </Card>
    );
}
