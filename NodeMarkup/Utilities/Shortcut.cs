﻿using ColossalFramework;
using ModsCommon.Utils;
using NodeMarkup.Tools;
using System;
using UnityEngine;

namespace NodeMarkup.Utilities
{
    public class NodeMarkupShortcut : Shortcut
    {
        public override string Label => Localize.ResourceManager.GetString(LabelKey, Localize.Culture);
        public ToolModeType ModeType { get; }
        public NodeMarkupShortcut(string name, string labelKey, InputKey key, Action action = null, ToolModeType modeType = ToolModeType.MakeItem) : base(Settings.SettingsFile, name, labelKey, key, action)
        {
            ModeType = modeType;
        }
        public override bool IsPressed(Event e) => (NodeMarkupTool.Instance.ModeType & ModeType) != ToolModeType.None && base.IsPressed(e);
        public override string ToString() => InputKey.ToLocalizedString("KEYNAME");
    }
}
