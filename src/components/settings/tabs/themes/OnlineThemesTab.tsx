/*
 * Endcord, a Discord client mod
 * Copyright (c) 2025 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { useSettings } from "@api/Settings";
import { Card } from "@components/Card";
import { Flex } from "@components/Flex";
import { Forms, TextArea, useState } from "@webpack/common";

export function OnlineThemesTab() {
    const settings = useSettings(["themeLinks"]);

    const [themeText, setThemeText] = useState(settings.themeLinks.join("\n"));

    // When the user leaves the online theme textbox, update the settings
    function onBlur() {
        settings.themeLinks = [...new Set(
            themeText
                .trim()
                .split(/\n+/)
                .map(s => s.trim())
                .filter(Boolean)
        )];
    }

    return (
        <Flex flexDirection="column" gap="1em">
            <Card variant="warning" defaultPadding>
                <Forms.FormText size="md">
                    這頁給進階使用。如果不好操作，請改用「本機主題」。
                </Forms.FormText>
            </Card>
            <Card>
                <Forms.FormTitle tag="h5">把 CSS 檔案網址貼在這裡</Forms.FormTitle>
                <Forms.FormText>一行一個網址</Forms.FormText>
                <Forms.FormText>行首可以加 @light 或 @dark，依照 Discord 的淺色或深色主題開關</Forms.FormText>
                <Forms.FormText>請用檔案的直接網址（raw 或 github.io）</Forms.FormText>
            </Card>

            <section>
                <Forms.FormTitle tag="h5">線上主題</Forms.FormTitle>
                <TextArea
                    value={themeText}
                    onChange={setThemeText}
                    className={"vc-settings-theme-links"}
                    placeholder="貼上主題網址..."
                    spellCheck={false}
                    onBlur={onBlur}
                    rows={10}
                />
            </section>
        </Flex>
    );
}
