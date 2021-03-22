﻿using ColossalFramework.Math;
using ColossalFramework.Threading;
using ColossalFramework.UI;
using ModsCommon.UI;
using NodeMarkup.Manager;
using NodeMarkup.Tools;
using NodeMarkup.Utilities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using static ToolBase;

namespace NodeMarkup.UI.Editors
{
    public class LinesEditor : Editor<LineItemsPanel, MarkupLine>
    {
        #region PROPERTIES

        public static Color HoverAlpha
        {
            get
            {
                var color = Colors.Hover;
                color.a = 128;
                return color;
            }
        }
        public override string Name => NodeMarkup.Localize.LineEditor_Lines;
        public override string EmptyMessage => NodeMarkup.Localize.LineEditor_EmptyMessage;
        public override Type SupportType { get; } = typeof(ISupportLines);

        private PropertyGroupPanel LineProperties { get; set; }
        private AddRuleButton AddButton { get; set; }

        public List<ILinePartEdge> SupportPoints { get; } = new List<ILinePartEdge>();
        public bool SupportRules => EditObject is MarkupRegularLine;
        public bool CanDivide => EditObject.IsSupportRules && SupportPoints.Count > 2;
        private bool AddRuleAvailable => CanDivide || EditObject?.Rules.Any() == false;
        public bool IsSplit => SupportRules && EditObject.PointPair.IsSplit;

        private RuleEdgeSelectPropertyPanel HoverPartEdgePanel { get; set; }
        private RulePanel HoverRulePanel { get; set; }

        private PartEdgeToolMode PartEdgeToolMode { get; }

        #endregion

        #region BASIC

        public LinesEditor()
        {
            ContentPanel.Content.autoLayoutPadding = new RectOffset(10, 10, 10, 10);
            PartEdgeToolMode = Tool.CreateToolMode<PartEdgeToolMode>();
            PartEdgeToolMode.Init(this);
        }

        protected override IEnumerable<MarkupLine> GetObjects() => Markup.Lines;
        protected override void OnObjectSelect(MarkupLine editObject)
        {
            ContentPanel.StopLayout();
            GetRuleEdges(editObject);

            AddLineProperties(EditObject);
            AddRulePanels(editObject);
            AddAddButton();
            ContentPanel.StartLayout();
        }
        protected override void OnObjectUpdate(MarkupLine editObject)
        {
            RefreshSelectedItem();
            GetRuleEdges(editObject);
            SetLinePropertiesVisible();
            RefreshRulePanels();
            SetAddButtonVisible();
        }
        protected override void OnObjectDelete(MarkupLine line)
        {
            Markup.RemoveLine(line);
            base.OnObjectDelete(line);
            if (line is MarkupCrosswalkLine crosswalkLine)
                Panel.DeleteCrosswalk(crosswalkLine.Crosswalk);
        }
        protected override void OnClear()
        {
            base.OnClear();
            HoverRulePanel = null;
            LineProperties = null;
            AddButton = null;
        }
        private void GetRuleEdges(MarkupLine editObject)
        {
            SupportPoints.Clear();
            SupportPoints.AddRange(editObject.RulesEdges);
        }
        private void AddLineProperties(MarkupLine editObject)
        {

            LineProperties = ComponentPool.Get<PropertyGroupPanel>(ContentPanel.Content);
            LineProperties.Init();

            if (editObject is MarkupRegularLine line)
            {
                var alignmentProperty = ComponentPool.Get<LineAlignmentPropertyPanel>(LineProperties);
                alignmentProperty.Text = "Line alignment";
                alignmentProperty.Init();
                alignmentProperty.SelectedObject = line.Alignment;
                alignmentProperty.OnSelectObjectChanged += (value) => line.Alignment.Value = value;
            }

            SetLinePropertiesVisible();
        }
        private void AddRulePanels(MarkupLine editObject)
        {
            foreach (var rule in editObject.Rules)
                AddRulePanel(rule);
        }

        private RulePanel AddRulePanel(MarkupLineRawRule rule)
        {
            var rulePanel = ComponentPool.Get<RulePanel>(ContentPanel.Content);
            rulePanel.Init(this, rule);
            rulePanel.OnHover += RuleMouseHover;
            rulePanel.OnEnter += RuleMouseLeave;
            return rulePanel;
        }
        private void RemoveRulePanel(RulePanel rulePanel)
        {
            if (HoverRulePanel == rulePanel)
                HoverRulePanel = null;

            ComponentPool.Free(rulePanel);
        }
        private void AddAddButton()
        {
            AddButton = ComponentPool.Get<AddRuleButton>(ContentPanel.Content);
            AddButton.Text = NodeMarkup.Localize.LineEditor_AddRuleButton;
            AddButton.Init();
            AddButton.OnButtonClick += AddRule;
            SetAddButtonVisible();
        }
        private void SetLinePropertiesVisible() => LineProperties.isVisible = IsSplit;
        private void SetAddButtonVisible()
        {
            AddButton.zOrder = -1;
            AddButton.isVisible = AddRuleAvailable;
        }

        private void AddRule()
        {
            if (!(EditObject is MarkupRegularLine regularLine))
                return;

            var newRule = regularLine.AddRule(CanDivide);
            var rulePanel = AddRulePanel(newRule);
            SetAddButtonVisible();

            ContentPanel.Content.ScrollToBottom();

            if (CanDivide && Settings.QuickRuleSetup)
                SetupRule(rulePanel);

            RefreshSelectedItem();
        }
        private void RefreshRulePanels()
        {
            var rulePanels = ContentPanel.Content.components.OfType<RulePanel>().ToArray();

            foreach (var rulePanel in rulePanels)
            {
                if (EditObject.ContainsRule(rulePanel.Rule))
                    rulePanel.Refresh();
                else
                    RemoveRulePanel(rulePanel);
            }
            foreach (var rule in EditObject.Rules)
            {
                if (!rulePanels.Any(r => r.Rule == rule))
                    AddRulePanel(rule);
            }
        }

        #endregion

        #region RULE

        private void SetupRule(RulePanel rulePanel) => SelectRuleEdge(rulePanel.From, (_) => SelectRuleEdge(rulePanel.To, (_) => SetStyle(rulePanel)));
        private bool SetStyle(RulePanel rulePanel)
        {
            var style = NodeMarkupTool.GetStyle(RegularLineStyle.RegularLineType.Dashed);
            rulePanel.Style.SelectedObject = style != Style.StyleType.EmptyLine ? style : (Style.StyleType)(int)RegularLineStyle.RegularLineType.Dashed;
            ContentPanel.Content.ScrollToBottom();
            return true;
        }
        public void DeleteRule(RulePanel rulePanel)
        {
            if (!(EditObject is MarkupRegularLine regularLine))
                return;

            if (Settings.DeleteWarnings && Settings.DeleteWarningsType == 0)
            {
                var messageBox = MessageBoxBase.ShowModal<YesNoMessageBox>();
                messageBox.CaptionText = NodeMarkup.Localize.LineEditor_DeleteRuleCaption;
                messageBox.MessageText = $"{NodeMarkup.Localize.LineEditor_DeleteRuleMessage}\n{NodeMarkupMessageBox.CantUndone}";
                messageBox.OnButton1Click = Delete;
            }
            else
                Delete();

            bool Delete()
            {
                regularLine.RemoveRule(rulePanel.Rule as MarkupLineRawRule<RegularLineStyle>);
                RemoveRulePanel(rulePanel);
                RefreshEditor();
                return true;
            }
        }
        public bool SelectRuleEdge(RuleEdgeSelectPropertyPanel selectPanel) => SelectRuleEdge(selectPanel, null);
        public bool SelectRuleEdge(RuleEdgeSelectPropertyPanel selectPanel, Func<Event, bool> afterAction)
        {
            if (Tool.Mode == PartEdgeToolMode && selectPanel == PartEdgeToolMode.SelectPanel)
            {
                Tool.SetDefaultMode();
                return true;
            }
            else
            {
                Tool.SetMode(PartEdgeToolMode);
                PartEdgeToolMode.SelectPanel = selectPanel;
                PartEdgeToolMode.AfterSelectPanel = afterAction;
                selectPanel.Focus();
                return false;
            }
        }
        public void HoverRuleEdge(RuleEdgeSelectPropertyPanel selectPanel) => HoverPartEdgePanel = selectPanel;
        public void LeaveRuleEdge(RuleEdgeSelectPropertyPanel selectPanel) => HoverPartEdgePanel = null;
        private void RuleMouseHover(RulePanel rulePanel, UIMouseEventParameter eventParam) => HoverRulePanel = rulePanel;
        private void RuleMouseLeave(RulePanel rulePanel, UIMouseEventParameter eventParam)
        {
            var uiView = rulePanel.GetUIView();
            var mouse = uiView.ScreenPointToGUI((eventParam.position + eventParam.moveDelta) / uiView.inputScale);
            var ruleRect = new Rect(ContentPanel.absolutePosition + rulePanel.relativePosition, rulePanel.size);
            var contentRect = new Rect(ContentPanel.absolutePosition, ContentPanel.size);

            if (eventParam.source == rulePanel || !ruleRect.Contains(mouse) || !contentRect.Contains(mouse))
                HoverRulePanel = null;
        }

        #endregion

        #region HANDLERS

        public override bool OnShortcut(Event e)
        {
            if (NodeMarkupTool.AddRuleShortcut.IsPressed(e) && AddRuleAvailable)
            {
                AddRule();
                return true;
            }
            else
                return false;
        }
        public override void Render(RenderManager.CameraInfo cameraInfo)
        {
            {
                ItemsPanel.HoverObject?.Render(cameraInfo, Colors.Hover, 2f);
                HoverRulePanel?.Rule.Render(cameraInfo, HoverAlpha, 2f);
                HoverPartEdgePanel?.Value?.Render(cameraInfo, Colors.Hover);
            }
        }

        #endregion
    }
    public class PartEdgeToolMode : BasePanelMode<LinesEditor, RuleEdgeSelectPropertyPanel, ILinePartEdge>
    {
        protected override bool IsHover => PointsSelector.IsHoverPoint;
        protected override ILinePartEdge Hover => PointsSelector.HoverPoint;
        public PointsSelector<ILinePartEdge> PointsSelector { get; set; }
        protected override void OnSetPanel() => PointsSelector = new PointsSelector<ILinePartEdge>(Editor.SupportPoints, SelectPanel.Position == EdgePosition.Start ? Colors.Green : Colors.Red);

        public override void Deactivate()
        {
            base.Deactivate();
            Editor.RefreshEditor();
        }
        public override void OnToolUpdate() => PointsSelector?.OnUpdate();
        public override string GetToolInfo()
        {
            var info = SelectPanel?.Position switch
            {
                EdgePosition.Start => Localize.LineEditor_InfoSelectFrom,
                EdgePosition.End => NodeMarkupTool.GetModifierToolTip<RegularLineStyle.RegularLineType>(Localize.LineEditor_InfoSelectTo),
                _ => string.Empty,
            };

            return info;
        }
        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo) => PointsSelector.Render(cameraInfo);
    }

    public class LineItemsPanel : ItemsGroupPanel<LineItem, MarkupLine, LineGroup, MarkupLine.LineType>
    {
        public override bool GroupingEnable => Settings.GroupLines.value;
        public override int Compare(MarkupLine x, MarkupLine y)
        {
            int result;

            if ((result = x.Start.Enter.CompareTo(y.Start.Enter)) == 0)
                if ((result = x.Start.Num.CompareTo(y.Start.Num)) == 0)
                    if ((result = x.End.Enter.CompareTo(y.End.Enter)) == 0)
                        result = x.End.Num.CompareTo(y.End.Num);

            return result;
        }
        public override int Compare(MarkupLine.LineType x, MarkupLine.LineType y) => x.CompareTo(y);

        protected override string GroupName(MarkupLine.LineType group) => group.Description();
        protected override MarkupLine.LineType SelectGroup(MarkupLine editObject) => editObject.Type;
    }
    public class LineItem : EditItem<MarkupLine, LineIcon>
    {
        private bool HasOverlapped { get; set; }
        public override Color32 NormalColor => HasOverlapped ? new Color32(246, 85, 85, 255) : base.NormalColor;
        public override Color32 HoveredColor => HasOverlapped ? new Color32(247, 100, 100, 255) : base.HoveredColor;
        public override Color32 PressedColor => HasOverlapped ? new Color32(248, 114, 114, 255) : base.PressedColor;
        public override Color32 FocusColor => HasOverlapped ? new Color32(249, 127, 127, 255) : base.FocusColor;
        public override void Refresh()
        {
            base.Refresh();

            SetIcon();

            HasOverlapped = Object.HasOverlapped;
            SetColors();
        }
        private void SetIcon()
        {
            if (!ShowIcon)
                return;

            var rules = Object.Rules.ToArray();
            Icon.Count = rules.Length;
            if (rules.Length == 1)
            {
                Icon.Type = rules[0].Style.Value.Type;
                Icon.StyleColor = rules[0].Style.Value.Color;
            }
        }
    }
    public class LineGroup : EditGroup<MarkupLine.LineType, LineItem, MarkupLine> { }
    public class AddRuleButton : ButtonPanel
    {
        public AddRuleButton()
        {
            Button.textScale = 1f;
        }
        protected override void SetSize() => Button.size = size;
    }
}
