/*
 * Endcord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { Card } from "@components/Card";
import { FormSwitch } from "@components/FormSwitch";
import { HeadingSecondary } from "@components/Heading";
import { Paragraph } from "@components/Paragraph";
import { SettingsTab, wrapTab } from "@components/settings/tabs/BaseTab";
import { getReadReceiptStatus, isPresenceReady, isTelemetryHooked, refreshPrivacyRuntime, settings } from "@plugins/privacy";
import { Margins } from "@utils/margins";
import { useEffect, useState } from "@webpack/common";

function PrivacySettings() {
    const s = settings.use(["privacyMode", "hideTyping", "hideReadReceipts", "hidePresence", "stripLinks", "blockTelemetry", "hideActivity"]);
    const disabled = !s.privacyMode;
    const [readReceipts, setReadReceipts] = useState(getReadReceiptStatus);
    const [presenceReady, setPresenceReady] = useState(isPresenceReady);
    const [telemetryHooked, setTelemetryHooked] = useState(isTelemetryHooked);

    useEffect(() => {
        refreshPrivacyRuntime();
        setReadReceipts(getReadReceiptStatus());
        setPresenceReady(isPresenceReady());
        setTelemetryHooked(isTelemetryHooked());
    }, []);

    return (
        <SettingsTab>
            <Paragraph className={Margins.bottom16}>
                隱私模式擋的是正在輸入、已讀、上線狀態、遊戲動態，以及 Discord 的分析與連結追蹤。訊息內容和帳號連線還是會送到 Discord。
            </Paragraph>

            <FormSwitch
                title="隱私模式"
                description="一次套用下面的保護。關掉後會還原。"
                value={s.privacyMode}
                onChange={(v: boolean) => s.privacyMode = v}
            />
            <FormSwitch
                title="不送出正在輸入"
                description="別人看不到你正在輸入。若你原本沒有開 Silent Typing，關掉這項會恢復送出。"
                value={s.hideTyping}
                disabled={disabled}
                onChange={(v: boolean) => s.hideTyping = v}
            />
            <FormSwitch
                title="不送出已讀"
                description="對方看不到你已讀。你自己的未讀數字仍會清掉。"
                value={s.hideReadReceipts}
                disabled={disabled}
                onChange={(v: boolean) => s.hideReadReceipts = v}
            />
            <FormSwitch
                title="對其他人顯示離線"
                description="朋友會看到你離線。關掉後回到原本的狀態。"
                value={s.hidePresence}
                disabled={disabled}
                onChange={(v: boolean) => s.hidePresence = v}
            />
            <FormSwitch
                title="清掉連結追蹤參數"
                description="送出與編輯訊息時去掉 utm、fbclid、gclid、msclkid、ttclid 這類參數。規則在本機，不會去下載清單。"
                value={s.stripLinks}
                disabled={disabled}
                onChange={(v: boolean) => s.stripLinks = v}
            />
            <FormSwitch
                title="擋下 Discord 追蹤"
                description="分析、指標、當機回報，以及用來推薦好友的 affinities 不會送出。登入和收發訊息不受影響。"
                value={s.blockTelemetry}
                disabled={disabled}
                onChange={(v: boolean) => s.blockTelemetry = v}
            />
            <FormSwitch
                title="不分享遊戲與 Spotify"
                description="別人看不到你正在玩的遊戲、Spotify 和自訂動態。關掉後回到原本的設定。"
                value={s.hideActivity}
                disabled={disabled}
                onChange={(v: boolean) => s.hideActivity = v}
                hideBorder
            />

            <HeadingSecondary className={Margins.top20}>已讀攔截</HeadingSecondary>
            <Card variant={readReceipts === "missing" ? "warning" : "info"} defaultPadding className={Margins.top8}>
                <Paragraph>
                    {readReceipts === "patched" && "已對到已讀送出。開啟時不會送到伺服器，你自己的未讀仍會清掉。"}
                    {readReceipts === "fallback" && "沒有對到 Discord 的已讀函式，已改攔網路送出。若對方仍看得到已讀，這項就是未生效。"}
                    {readReceipts === "missing" && "未生效。這版 Discord 的已讀送出還沒對到。"}
                </Paragraph>
            </Card>

            <HeadingSecondary className={Margins.top20}>分析與當機回報</HeadingSecondary>
            <Card variant={s.privacyMode && s.blockTelemetry && !telemetryHooked ? "warning" : "success"} defaultPadding className={Margins.top8}>
                <Paragraph>
                    {s.privacyMode && s.blockTelemetry && telemetryHooked && "已攔下分析、指標、當機回報和 affinities。NoTrack 也仍會關掉 Discord 內建的分析。"}
                    {s.privacyMode && s.blockTelemetry && !telemetryHooked && "追蹤攔截還沒掛上。重新開啟 Discord 後再看一次。"}
                    {!(s.privacyMode && s.blockTelemetry) && "這項目前關掉。NoTrack 仍會停用 Discord 內建的分析與當機回報。"}
                </Paragraph>
            </Card>

            {s.privacyMode && s.hidePresence && !presenceReady && (
                <Card variant="warning" defaultPadding className={Margins.top16}>
                    <Paragraph>上線狀態還沒對到 Discord 的設定，離線顯示目前未生效。重新開啟 Discord 後再看一次。</Paragraph>
                </Card>
            )}
        </SettingsTab>
    );
}

export default wrapTab(PrivacySettings, "隱私");
