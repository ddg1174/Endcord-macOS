/*
 * Endcord, a Discord client mod
 * Copyright (c) 2025 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { Divider } from "@components/Divider";
import { ErrorCard } from "@components/ErrorCard";
import { Link } from "@components/Link";
import { CspBlockedUrls, useCspErrors } from "@utils/cspViolations";
import { Margins } from "@utils/margins";
import { classes } from "@utils/misc";
import { relaunch } from "@utils/native";
import { useForceUpdater } from "@utils/react";
import { Button, ConfirmModal, Forms, openModal } from "@webpack/common";

export function CspErrorCard() {
    if (IS_WEB) return null;

    const errors = useCspErrors();
    const forceUpdate = useForceUpdater();

    if (!errors.length) return null;

    const isImgurHtmlDomain = (url: string) => url.startsWith("https://imgur.com/");

    const allowUrl = async (url: string) => {
        const { origin: baseUrl, host } = new URL(url);

        const result = await EndcordNative.csp.requestAddOverride(baseUrl, ["connect-src", "img-src", "style-src", "font-src"], "Endcord Themes");
        if (result !== "ok") return;

        CspBlockedUrls.forEach(url => {
            if (new URL(url).host === host) {
                CspBlockedUrls.delete(url);
            }
        });

        forceUpdate();

        openModal(props => (
            <ConfirmModal
                {...props}
                title="需要重新啟動"
                subtitle="要重新啟動才會套用這項變更"
                confirmText="現在重新啟動"
                cancelText="稍後"
                variant="primary"
                onConfirm={relaunch}
            />
        ));
    };

    const hasImgurHtmlDomain = errors.some(isImgurHtmlDomain);

    return (
        <ErrorCard>
            <Forms.FormTitle tag="h5">被擋住的資源</Forms.FormTitle>
            <Forms.FormText>有些圖片、樣式或字型來自不允許的網域，所以被擋住了。</Forms.FormText>
            <Forms.FormText>建議改放到 GitHub 或 Imgur。如果你完全信任那個網域，也可以允許它。</Forms.FormText>
            <Forms.FormText>
                允許之後，要從工作列或工作管理員完全關掉 {IS_DISCORD_DESKTOP ? "Discord" : "Vesktop"} 再打開，才會生效。
            </Forms.FormText>

            <Forms.FormTitle tag="h5" className={classes(Margins.top16, Margins.bottom8)}>被擋住的網址</Forms.FormTitle>
            <div className="vc-settings-csp-list">
                {errors.map((url, i) => (
                    <div key={url}>
                        {i !== 0 && <Divider className={Margins.bottom8} />}
                        <div className="vc-settings-csp-row">
                            <Link href={url}>{url}</Link>
                            <Button color={Button.Colors.PRIMARY} onClick={() => allowUrl(url)} disabled={isImgurHtmlDomain(url)}>
                                允許
                            </Button>
                        </div>
                    </div>
                ))}
            </div>

            {hasImgurHtmlDomain && (
                <>
                    <Divider className={classes(Margins.top8, Margins.bottom16)} />
                    <Forms.FormText>
                        Imgur 請用 <code>https://i.imgur.com/...</code> 這種直接網址。
                    </Forms.FormText>
                    <Forms.FormText>在圖片上按右鍵，選「複製圖片網址」。</Forms.FormText>
                </>
            )}
        </ErrorCard>
    );
}
