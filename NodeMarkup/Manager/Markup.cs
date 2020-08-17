﻿using ColossalFramework.Math;
using NodeMarkup.UI;
using NodeMarkup.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace NodeMarkup.Manager
{
    public class Markup : IToXml
    {
        #region STATIC

        public static string XmlName { get; } = "M";

        #endregion

        #region PROPERTIES

        public string XmlSection => XmlName;
        public ushort Id { get; }
        private Vector4 Index { get; }
        public float Height { get; private set; }
        List<Enter> EntersList { get; set; } = new List<Enter>();
        Dictionary<ulong, MarkupLine> LinesDictionary { get; } = new Dictionary<ulong, MarkupLine>();
        Dictionary<MarkupLinePair, MarkupLinesIntersect> LineIntersects { get; } = new Dictionary<MarkupLinePair, MarkupLinesIntersect>(MarkupLinePair.Comparer);
        List<MarkupFiller> FillersList { get; } = new List<MarkupFiller>();
        List<Bezier3> ContourParts { get; set; } = new List<Bezier3>();

        public bool NeedRecalculateBatches { get; set; }
        public RenderBatch[] RenderBatches { get; private set; } = new RenderBatch[0];

        public IEnumerable<MarkupLine> Lines => LinesDictionary.Values;
        public IEnumerable<Enter> Enters => EntersList;
        public IEnumerable<MarkupFiller> Fillers => FillersList;
        public IEnumerable<MarkupLinesIntersect> Intersects => GetAllIntersect().Where(i => i.IsIntersect);
        public IEnumerable<Bezier3> Contour => ContourParts;

        #endregion

        public Markup(ushort nodeId)
        {
            Id = nodeId;
            Index = RenderManager.GetColorLocation((uint)(86016 + Id)) + new Vector4(0, 0, 0, 1);
            Update();
        }

        #region UPDATE

        public void Update()
        {
            UpdateEnters();
            UpdateLines();
            UpdateFillers();

            RecalculateDashes();
        }

        private void UpdateEnters()
        {
            var node = Utilities.GetNode(Id);
            Height = node.m_position.y;

            var oldEnters = EntersList;
            var exists = oldEnters.Select(e => e.Id).ToList();
            var update = node.SegmentsId().ToList();

            var still = exists.Intersect(update).ToArray();
            var delete = exists.Except(still).ToArray();
            var add = update.Except(still).ToArray();

            var newEnters = still.Select(id => oldEnters.Find(e => e.Id == id)).ToList();
            newEnters.AddRange(add.Select(id => new Enter(this, id)));
            newEnters.Sort((e1, e2) => e1.AbsoluteAngle.CompareTo(e2.AbsoluteAngle));

            if (delete.Length == 1 && add.Length == 1 && oldEnters.Find(e => e.Id == delete[0]).PointCount == newEnters.Find(e => e.Id == add[0]).PointCount)
            {
                var map = new Dictionary<ObjectId, ObjectId>()
                {
                    {new ObjectId() {Segment = delete[0] },  new ObjectId() {Segment = add[0] }}
                };

                var currentData = ToXml();
                EntersList = newEnters;
                Clear();
                FromXml(Mod.Version, currentData, map);
            }
            else
                EntersList = newEnters;

            foreach (var enter in EntersList)
                enter.Update();

            UpdateNodeСontour();

            foreach (var enter in EntersList)
                enter.UpdatePoints();
        }
        private void UpdateNodeСontour()
        {
            var contourParts = new List<Bezier3>();

            for(var i = 0; i < EntersList.Count; i += 1)
            {
                var prev = EntersList[i];
                var currentBezier = new Bezier3()
                {
                    a = prev.LeftSide,
                    d = prev.RightSide
                };
                var currentDir = (currentBezier.d - currentBezier.a).normalized;
                NetSegment.CalculateMiddlePoints(currentBezier.a, currentDir, currentBezier.d, -currentDir, true, true, out currentBezier.b, out currentBezier.c);
                contourParts.Add(currentBezier);


                var next = GetNextEnter(i);
                var betweenBezier = new Bezier3()
                {
                    a = prev.RightSide,
                    d = next.LeftSide
                };
                NetSegment.CalculateMiddlePoints(betweenBezier.a, prev.NormalDir, betweenBezier.d, next.NormalDir, true, true, out betweenBezier.b, out betweenBezier.c);
                contourParts.Add(betweenBezier);
            }

            ContourParts = contourParts;
        }

        private void UpdateLines()
        {
            foreach (var line in LinesDictionary.Values.ToArray())
            {
                if (ContainsEnter(line.Start.Enter.Id) && ContainsEnter(line.End.Enter.Id))
                    line.UpdateTrajectory();
                else
                    RemoveLine(line);
            }
        }
        private void UpdateFillers()
        {
            foreach (var filler in FillersList)
                filler.Update();
        }

        public void Update(MarkupPoint point)
        {
            point.Update();

            foreach (var line in GetPointLines(point))
            {
                line.UpdateTrajectory();
            }
            foreach (var filler in GetPointFillers(point))
            {
                filler.Update();
            }

            RecalculateDashes();
        }
        public void Update(MarkupLine line, bool updateIntersect = false, bool updateFillers = false)
        {
            line.UpdateTrajectory();
            line.RecalculateDashes();
            if(updateIntersect)
            {
                foreach (var intersect in GetExistIntersects(line, true).ToArray())
                {
                    LineIntersects.Remove(intersect.Pair);
                    Update(intersect.Pair.GetOther(line));
                }
            }
            if(updateFillers)
            {
                foreach (var filler in GetLineFillers(line))
                    Update(filler);
            }

            NeedRecalculateBatches = true;
        }
        public void Update(MarkupFiller filler)
        {
            filler.RecalculateDashes();
            NeedRecalculateBatches = true;
        }

        public void Clear()
        {
            LinesDictionary.Clear();
            FillersList.Clear();

            RecalculateDashes();
        }

        #endregion

        #region RECALCULATE

        public void RecalculateDashes()
        {
            LineIntersects.Clear();
            foreach (var line in Lines)
            {
                line.RecalculateDashes();
            }
            foreach (var filler in Fillers)
            {
                filler.RecalculateDashes();
            }
            NeedRecalculateBatches = true;
        }
        public void RecalculateBatches()
        {
            var dashes = new List<MarkupStyleDash>();
            dashes.AddRange(Lines.SelectMany(l => l.Dashes));
            dashes.AddRange(Fillers.SelectMany(f => f.Dashes));
            RenderBatches = RenderBatch.FromDashes(dashes, Index).ToArray();
        }

        #endregion

        #region LINES

        public bool ExistConnection(MarkupPointPair pointPair) => LinesDictionary.ContainsKey(pointPair.Hash);
        public MarkupLine ToggleConnection(MarkupPointPair pointPair, Style.StyleType style)
        {
            if (LinesDictionary.TryGetValue(pointPair.Hash, out MarkupLine line))
            {
                RemoveConnect(line);
                return null;
            }
            else
            {
                if (pointPair.IsNormal && !EarlyAccess.CheckFunctionAccess(Localize.EarlyAccess_Function_PerpendicularLines))
                    return null;

                line = MarkupLine.FromStyle(this, pointPair, style);
                LinesDictionary[pointPair.Hash] = line;
                NeedRecalculateBatches = true;
                return line;
            }
        }
        public void RemoveConnect(MarkupLine line)
        {
            RemoveLine(line);
            RecalculateDashes();
        }
        private void RemoveLine(MarkupLine line)
        {
            foreach (var intersect in GetExistIntersects(line, false).ToArray())
            {
                if (intersect.Pair.GetOther(line) is MarkupRegularLine regularLine)
                    regularLine.RemoveRules(line);

                LineIntersects.Remove(intersect.Pair);
            }
            foreach (var filler in GetLineFillers(line).ToArray())
            {
                FillersList.Remove(filler);
            }

            LinesDictionary.Remove(line.PointPair.Hash);
        }

        #endregion

        #region GET & CONTAINS

        public bool TryGetLine(ulong lineId, out MarkupLine line) => LinesDictionary.TryGetValue(lineId, out line);
        public bool TryGetLine<LineType>(ulong lineId, out LineType line)
            where LineType : MarkupLine
        {
            if (LinesDictionary.TryGetValue(lineId, out MarkupLine rawLine) && rawLine is LineType)
            {
                line = rawLine as LineType;
                return true;
            }
            else
            {
                line = null;
                return false;
            }
        }
        public bool TryGetEnter(ushort enterId, out Enter enter)
        {
            enter = EntersList.Find(e => e.Id == enterId);
            return enter != null;
        }
        public bool ContainsEnter(ushort enterId) => EntersList.Find(e => e.Id == enterId) != null;
        public bool ContainsLine(MarkupPointPair pointPair) => LinesDictionary.ContainsKey(pointPair.Hash);

        public IEnumerable<MarkupLinesIntersect> GetExistIntersects(MarkupLine line, bool onlyIntersect = false) 
            => LineIntersects.Values.Where(i => i.Pair.ContainLine(line) && (!onlyIntersect || i.IsIntersect));
        public IEnumerable<MarkupLinesIntersect> GetIntersects(MarkupLine line)
        {
            foreach(var otherLine in Lines)
            {
                if (otherLine != line)
                    yield return GetIntersect(new MarkupLinePair(line, otherLine));
            }
        }

        public MarkupLinesIntersect GetIntersect(MarkupLine first, MarkupLine second) => GetIntersect(new MarkupLinePair(first, second));
        public MarkupLinesIntersect GetIntersect(MarkupLinePair linePair)
        {
            if (!LineIntersects.TryGetValue(linePair, out MarkupLinesIntersect intersect))
            {
                intersect = MarkupLinesIntersect.Calculate(linePair);
                LineIntersects.Add(linePair, intersect);
            }

            return intersect;
        }
        public IEnumerable<MarkupLinesIntersect> GetAllIntersect()
        {
            var lines = Lines.ToArray();
            for (var i = 0; i < lines.Length; i += 1)
            {
                for (var j = i + 1; j < lines.Length; j += 1)
                {
                    yield return GetIntersect(new MarkupLinePair(lines[i], lines[j]));
                }
            }
        }

        public Enter GetNextEnter(Enter current) => GetNextEnter(EntersList.IndexOf(current));
        public Enter GetNextEnter(int index) => EntersList[index == EntersList.Count - 1 ? 0 : index + 1];
        public Enter GetPrevEnter(Enter current) => GetPrevEnter(EntersList.IndexOf(current));
        public Enter GetPrevEnter(int index) => EntersList[index == 0 ? EntersList.Count - 1 : index - 1];

        public IEnumerable<MarkupLine> GetPointLines(MarkupPoint point) => LinesDictionary.Values.Where(l => l.ContainsPoint(point));
        public IEnumerable<MarkupFiller> GetLineFillers(MarkupLine line) => FillersList.Where(f => f.ContainsLine(line));
        public IEnumerable<MarkupFiller> GetPointFillers(MarkupPoint point) => FillersList.Where(f => f.ContainsPoint(point));

        #endregion

        #region FILLERS

        public void AddFiller(MarkupFiller filler)
        {
            FillersList.Add(filler);
            filler.RecalculateDashes();
            NeedRecalculateBatches = true;
        }
        public void RemoveFiller(MarkupFiller filler)
        {
            FillersList.Remove(filler);
            NeedRecalculateBatches = true;
        }

        #endregion

        #region XML

        public XElement ToXml()
        {
            var config = new XElement(XmlSection,
                new XAttribute(nameof(Id), Id.ToString())
            );

            foreach (var enter in Enters)
            {
                foreach (var point in enter.Points)
                {
                    var pointConfig = point.ToXml();
                    config.Add(pointConfig);
                }
                //foreach (var point in enter.Crosswalks)
                //{
                //    var pointConfig = point.ToXml();
                //    config.Add(pointConfig);
                //}
            }
            foreach (var line in Lines)
            {
                var lineConfig = line.ToXml();
                config.Add(lineConfig);
            }
            foreach (var filler in Fillers)
            {
                var fillerConfig = filler.ToXml();
                config.Add(fillerConfig);
            }

            return config;
        }
        public static bool FromXml(Version version, XElement config, out Markup markup)
        {
            var nodeId = config.GetAttrValue<ushort>(nameof(Id));
            markup = MarkupManager.Get(nodeId);
            markup.FromXml(version, config);
            return true;
        }
        public void FromXml(Version version, XElement config, Dictionary<ObjectId, ObjectId> map = null)
        {
            if (version < new Version("1.2"))
                map = VersionMigration.Befor1_2(this, map);

            foreach (var pointConfig in config.Elements(MarkupPoint.XmlName))
                MarkupPoint.FromXml(pointConfig, this, map);

            var toInit = new Dictionary<MarkupLine, XElement>();
            foreach (var lineConfig in config.Elements(MarkupLine.XmlName))
            {
                if (MarkupLine.FromXml(lineConfig, this, map, out MarkupLine line))
                {
                    LinesDictionary[line.Id] = line;
                    toInit[line] = lineConfig;
                }
            }

            foreach (var pair in toInit)
                pair.Key.FromXml(pair.Value, map);

            foreach (var fillerConfig in config.Elements(MarkupFiller.XmlName))
            {
                if (MarkupFiller.FromXml(fillerConfig, this, map, out MarkupFiller filler))
                    FillersList.Add(filler);
            }
        }

        #endregion XML

        public enum Item
        {
            [Description(nameof(Localize.LineStyle_RegularGroup))]
            RegularLine = 0x100,

            [Description(nameof(Localize.LineStyle_StopGroup))]
            StopLine = 0x200,

            [Description(nameof(Localize.FillerStyle_Group))]
            Filler = 0x400,

            [Description(nameof(Localize.CrosswalkStyle_Group))]
            Crosswalk = 0x800,
        }
    }
}
