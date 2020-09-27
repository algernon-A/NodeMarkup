﻿using ColossalFramework.Math;
using NodeMarkup.UI.Editors;
using NodeMarkup.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace NodeMarkup.Manager
{
    public class MarkupFiller : IUpdate, IDeletable, IToXml
    {
        public static string XmlName { get; } = "F";

        public string DeleteCaptionDescription => Localize.FillerEditor_DeleteCaptionDescription;
        public string DeleteMessageDescription => Localize.FillerEditor_DeleteMessageDescription;

        public Markup Markup { get; }
        public FillerContour Contour { get; }

        FillerStyle _style;
        public FillerStyle Style
        {
            get => _style;
            set
            {
                _style = value;
                _style.OnStyleChanged = OnStyleChanged;
                OnStyleChanged();
            }
        }
        public MarkupStyleDash[] Dashes { get; private set; } = new MarkupStyleDash[0];
        public bool IsMedian => Contour.Parts.Any(p => p.Line is MarkupEnterLine);

        public string XmlSection => XmlName;

        public MarkupFiller(FillerContour contour, FillerStyle style)
        {
            Contour = contour;
            Markup = Contour.Markup;
            Style = style;
        }
        public MarkupFiller(FillerContour contour, Style.StyleType fillerType) : this(contour, TemplateManager.GetDefault<FillerStyle>(fillerType)) { }

        private void OnStyleChanged() => Markup.Update(this, true);
        public bool ContainsLine(MarkupLine line) => Contour.Parts.Any(p => !(p.Line is MarkupEnterLine) && p.Line.PointPair == line.PointPair);
        public bool ContainsPoint(MarkupPoint point) => Contour.Vertices.Any(s => s is EnterFillerVertex vertex && vertex.Point == point);

        public void Update(bool onlySelfUpdate = false)
        {
            foreach (var part in Contour.Parts)
            {
                if (part.Line is MarkupEnterLine fakeLine)
                    fakeLine.Update(true);
            }
        }
        public void RecalculateDashes() => Dashes = Style.Calculate(this).ToArray();

        public Dependences GetDependences() => new Dependences();

        public XElement ToXml()
        {
            var config = new XElement(XmlSection, Style.ToXml());
            foreach (var supportPoint in Contour.Vertices)
            {
                config.Add(supportPoint.ToXml());
            }
            return config;
        }
        public static bool FromXml(XElement config, Markup markup, PasteMap map, out MarkupFiller filler)
        {
            if (!(config.Element(Manager.Style.XmlName) is XElement styleConfig) || !Manager.Style.FromXml(styleConfig, map, out FillerStyle style))
            {
                filler = default;
                return false;
            }

            var contour = new FillerContour(markup);

            foreach (var supportConfig in config.Elements(FillerVertex.XmlName))
            {
                if (FillerVertex.FromXml(supportConfig, markup, map, out IFillerVertex vertex))
                    contour.Add(vertex);
                else
                {
                    filler = default;
                    return false;
                }
            }
            if(contour.First == null)
            {
                filler = default;
                return false;
            }

            contour.Add(contour.First);

            filler = new MarkupFiller(contour, style);
            return true;
        }

        public override string ToString() => Math.Abs(GetHashCode()).ToString();
    }
    public class FillerLinePart : MarkupLinePart
    {
        public override string XmlSection => throw new NotImplementedException();
        public new IFillerVertex From
        {
            get => base.From as IFillerVertex;
            set => base.From = value;
        }
        public new IFillerVertex To
        {
            get => base.To as IFillerVertex;
            set => base.To = value;
        }
        public FillerLinePart(MarkupLine line, IFillerVertex from, IFillerVertex to) : base(line, from, to) { }
    }
}

