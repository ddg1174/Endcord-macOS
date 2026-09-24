/*
 * Endcord, a Discord client mod
 * Copyright (c) 2026 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import "./styles.css";

import { BaseText } from "@components/BaseText";
import { Card } from "@components/Card";
import { Flex } from "@components/Flex";
import { Link } from "@components/Link";
import { Margins } from "@components/margins";
import { Paragraph } from "@components/Paragraph";
import { SettingsTab, wrapTab } from "@components/settings/tabs/BaseTab";
import { getStylusWebStoreUrl } from "@utils/web";
import { Forms, React, TabBar, useState } from "@webpack/common";

import { CspErrorCard } from "./CspErrorCard";
import { CustomBackgroundCard } from "./CustomBackgroundCard";
import { LocalThemesTab } from "./LocalThemesTab";
import { OnlineThemesTab } from "./OnlineThemesTab";

const enum ThemeTab {
    LOCAL,
    ONLINE
}

function ThemesTab() {
    const [currentTab, setCurrentTab] = useState(ThemeTab.LOCAL);

    return (
        <SettingsTab>
            <TabBar
                type="top"
                look="brand"
                className="vc-settings-tab-bar"
                selectedItem={currentTab}
                onItemSelect={setCurrentTab}
            >
                <TabBar.Item
                    className="vc-settings-tab-bar-item"
                    id={ThemeTab.LOCAL}
                >
                    本機主題
                </TabBar.Item>
                <TabBar.Item
                    className="vc-settings-tab-bar-item"
                    id={ThemeTab.ONLINE}
                >
                    線上主題
                </TabBar.Item>
            </TabBar>

            <Flex flexDirection="column" gap="1em">
                <CspErrorCard />

                <Card variant="warning">
                    <BaseText tag="h3" size="md" weight="medium" className={Margins.bottom8}>主題效能</BaseText>
                    <Paragraph>
                        主題和自訂 CSS 可能造成明顯卡頓。如果 Discord 變慢，先關掉主題和 CSS 看看是不是它們造成的。最常見的原因是 <code>:has()</code>。
                    </Paragraph>
                </Card>

                <CustomBackgroundCard />

                {currentTab === ThemeTab.LOCAL && <LocalThemesTab />}
                {currentTab === ThemeTab.ONLINE && <OnlineThemesTab />}
            </Flex>
        </SettingsTab>
    );
}

function UserscriptThemesTab() {
    return (
        <SettingsTab>
            <Card variant="danger">
                <Forms.FormTitle tag="h5">使用者腳本不支援主題</Forms.FormTitle>

                <Forms.FormText>
                    可以改用 <Link href={getStylusWebStoreUrl()}>Stylus 擴充功能</Link> 安裝主題。
                </Forms.FormText>
            </Card>
        </SettingsTab>
    );
}

export default IS_USERSCRIPT
    ? wrapTab(UserscriptThemesTab, "主題")
    : wrapTab(ThemesTab, "主題");
