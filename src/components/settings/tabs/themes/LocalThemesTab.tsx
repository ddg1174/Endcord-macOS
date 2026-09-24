/*
 * Endcord, a Discord client mod
 * Copyright (c) 2025 Vendicated and contributors
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

import { isPluginEnabled } from "@api/PluginManager";
import { Settings, useSettings } from "@api/Settings";
import { Card } from "@components/Card";
import { Flex } from "@components/Flex";
import { FolderIcon, PaintbrushIcon, PencilIcon, PlusIcon, RestartIcon } from "@components/Icons";
import { Link } from "@components/Link";
import { QuickAction, QuickActionCard } from "@components/settings/QuickAction";
import { openPluginModal } from "@components/settings/tabs/plugins/PluginModal";
import { UserThemeHeader } from "@main/themes";
import ClientThemePlugin from "@plugins/clientTheme";
import { classNameFactory } from "@utils/css";
import { findLazy } from "@webpack";
import { Forms, useEffect, useRef, useState } from "@webpack/common";
import type { ComponentType, Ref, SyntheticEvent } from "react";

import { ThemeCard } from "./ThemeCard";

const cl = classNameFactory("vc-settings-theme-");

type FileInput = ComponentType<{
    ref: Ref<HTMLInputElement>;
    onChange: (e: SyntheticEvent<HTMLInputElement>) => void;
    multiple?: boolean;
    filters?: { name?: string; extensions: string[]; }[];
}>;

const FileInput: FileInput = findLazy(m => m.prototype?.activateUploadDialogue && m.prototype.setRef);

// When a local theme is enabled/disabled, update the settings
function onLocalThemeChange(fileName: string, value: boolean) {
    if (value) {
        if (Settings.enabledThemes.includes(fileName)) return;
        Settings.enabledThemes = [...Settings.enabledThemes, fileName];
    } else {
        Settings.enabledThemes = Settings.enabledThemes.filter(f => f !== fileName);
    }
}

async function onFileUpload(e: SyntheticEvent<HTMLInputElement>) {
    e.stopPropagation();
    e.preventDefault();

    if (!e.currentTarget?.files?.length) return;
    const { files } = e.currentTarget;

    const uploads = Array.from(files, file => {
        const { name } = file;
        if (!name.endsWith(".css")) return;

        return new Promise<void>((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => {
                EndcordNative.themes.uploadTheme(name, reader.result as string)
                    .then(resolve)
                    .catch(reject);
            };
            reader.readAsText(file);
        });
    });

    await Promise.all(uploads);
}

export function LocalThemesTab() {
    const settings = useSettings(["enabledThemes"]);

    const fileInputRef = useRef<HTMLInputElement>(null);

    const [userThemes, setUserThemes] = useState<UserThemeHeader[] | null>(null);

    useEffect(() => {
        refreshLocalThemes();
    }, []);

    async function refreshLocalThemes() {
        const themes = await EndcordNative.themes.getThemesList();
        setUserThemes(themes);
    }

    return (
        <Flex flexDirection="column" gap="1em">
            <Card>
                <Forms.FormTitle tag="h5">找主題</Forms.FormTitle>
                <div style={{ marginBottom: ".5em", display: "flex", flexDirection: "column" }}>
                    <Link style={{ marginRight: ".5em" }} href="https://betterdiscord.app/themes">
                        BetterDiscord 主題
                    </Link>
                    <Link href="https://github.com/search?q=discord+theme">GitHub</Link>
                </div>
                <Forms.FormText>如果用 BetterDiscord 網站，按 Download，再把下載的 .theme.css 放到主題資料夾。</Forms.FormText>
            </Card>

            <Card>
                <Forms.FormTitle tag="h5">外部資源</Forms.FormTitle>
                <Forms.FormText>為了安全，大多數網站的樣式、字型、圖片不能直接載入。</Forms.FormText>
                <Forms.FormText>請把素材放在 GitHub、GitLab、Codeberg、Imgur、Discord 或 Google Fonts。</Forms.FormText>
            </Card>

            <section>
                <Forms.FormTitle tag="h5">本機主題</Forms.FormTitle>
                <QuickActionCard>
                    <>
                        {IS_WEB ?
                            (
                                <QuickAction
                                    text={
                                        <span style={{ position: "relative" }}>
                                            上傳主題
                                            <FileInput
                                                ref={fileInputRef}
                                                onChange={async e => {
                                                    await onFileUpload(e);
                                                    refreshLocalThemes();
                                                }}
                                                multiple={true}
                                                filters={[{ extensions: ["css"] }]}
                                            />
                                        </span>
                                    }
                                    Icon={PlusIcon}
                                />
                            ) : (
                                <QuickAction
                                    text="開啟主題資料夾"
                                    action={() => EndcordNative.themes.openFolder()}
                                    Icon={FolderIcon}
                                />
                            )}
                        <QuickAction
                            text="重新載入主題"
                            action={refreshLocalThemes}
                            Icon={RestartIcon}
                        />
                        <QuickAction
                            text="編輯 QuickCSS"
                            action={() => EndcordNative.quickCss.openEditor()}
                            Icon={PaintbrushIcon}
                        />

                        {isPluginEnabled(ClientThemePlugin.name) && (
                            <QuickAction
                                text="編輯客戶端主題"
                                action={() => openPluginModal(ClientThemePlugin)}
                                Icon={PencilIcon}
                            />
                        )}
                    </>
                </QuickActionCard>

                <div className={cl("grid")}>
                    {userThemes?.map(theme => (
                        <ThemeCard
                            key={theme.fileName}
                            enabled={settings.enabledThemes.includes(theme.fileName)}
                            onChange={enabled => onLocalThemeChange(theme.fileName, enabled)}
                            onDelete={async () => {
                                onLocalThemeChange(theme.fileName, false);
                                await EndcordNative.themes.deleteTheme(theme.fileName);
                                refreshLocalThemes();
                            }}
                            theme={theme}
                        />
                    ))}
                </div>
            </section>
        </Flex>
    );
}
