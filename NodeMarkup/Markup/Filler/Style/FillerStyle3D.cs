﻿using ColossalFramework.UI;
using ModsCommon;
using ModsCommon.UI;
using ModsCommon.Utilities;
using NodeMarkup.UI.Editors;
using NodeMarkup.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace NodeMarkup.Manager
{
    public abstract class Filler3DStyle : FillerStyle
    {
        public Filler3DStyle(Color32 color, float width, float lineOffset, float medianOffset) : base(color, width, lineOffset, medianOffset) { }
    }
    public abstract class TriangulationFillerStyle : Filler3DStyle
    {
        protected abstract MaterialType MaterialType { get; }

        public float MinAngle => 5f;
        public float MinLength => 1f;
        public float MaxLength => 10f;
        public PropertyValue<float> Elevation { get; }
        public PropertyValue<float> CornerRadius { get; }
        public PropertyValue<float> MedianCornerRadius { get; }

        public TriangulationFillerStyle(Color32 color, float width, float lineOffset, float medianOffset, float elevation, float cornerRadius, float medianCornerRadius) : base(color, width, lineOffset, medianOffset)
        {
            Elevation = GetElevationProperty(elevation);
            CornerRadius = GetCornerRadiusProperty(cornerRadius);
            MedianCornerRadius = GetMedianCornerRadiusProperty(medianCornerRadius);
        }

        public override void CopyTo(FillerStyle target)
        {
            base.CopyTo(target);
            if (target is TriangulationFillerStyle triangulationTarget)
            {
                triangulationTarget.Elevation.Value = Elevation;
                triangulationTarget.CornerRadius.Value = CornerRadius;
                triangulationTarget.MedianCornerRadius.Value = MedianCornerRadius;
            }
        }

        protected override List<List<FillerContour.Part>> GetContours(MarkupFiller filler)
        {
            var contours = base.GetContours(filler);

            var roundedContours = new List<List<FillerContour.Part>>();
            foreach (var contour in contours)
            {
                var rounded = StyleHelper.SetCornerRadius(contour, CornerRadius, MedianCornerRadius);
                roundedContours.Add(rounded);
            }

            return roundedContours;
        }
        public override IEnumerable<IStyleData> Calculate(MarkupFiller filler, List<List<FillerContour.Part>> contours, MarkupLOD lod)
        {
            foreach (var contour in contours)
            {
                var trajectories = contour.Select(i => i.Trajectory).ToList();
                if (trajectories.GetDirection() == TrajectoryHelper.Direction.CounterClockWise)
                    trajectories = trajectories.Select(t => t.Invert()).Reverse().ToList();

                var parts = GetParts(trajectories, lod);

                var points = parts.SelectMany(p => p).Select(t => t.StartPosition).ToArray();
                if (points.Length < 3)
                    yield break;

                var triangles = Triangulator.Triangulate(points, TrajectoryHelper.Direction.ClockWise);
                if (triangles == null)
                    yield break;

                yield return new MarkupStylePolygonTopMesh(filler.Markup.Height, Elevation, points, triangles, MaterialType);
                yield return new MarkupStylePolygonSideMesh(filler.Markup.Height, Elevation, parts.Select(g => g.Count).ToArray(), points, MaterialType.Pavement);
            }
        }
        private List<List<ITrajectory>> GetParts(List<ITrajectory> trajectories, MarkupLOD lod)
        {
            var parts = trajectories.Select(t => StyleHelper.CalculateSolid(t, lod, (tr) => tr, MinAngle, MinLength, MaxLength)).ToList();
            for (var i = 0; i < parts.Count; i += 1)
            {
                var xm = (i - 1 + parts.Count) % parts.Count;
                var x = i;
                var y = (i + 1) % parts.Count;
                var yp = (i + 2) % parts.Count;

                if (FindIntersects(parts[x], parts[y], true, 1))
                    continue;
                if (parts.Count > 3 && parts[y].Count == 1 && FindIntersects(parts[x], parts[yp], true, 0))
                {
                    parts.RemoveAt(y);
                    continue;
                }
                if (FindIntersects(parts[y], parts[x], false, 1))
                    continue;
                if (parts.Count > 3 && parts[x].Count == 1 && FindIntersects(parts[y], parts[xm], false, 0))
                {
                    parts.RemoveAt(x);
                    i -= 1;
                    continue;
                }
            }

            return parts;
        }
        private bool FindIntersects(List<ITrajectory> A, List<ITrajectory> B, bool isClockWise, int skip)
        {
            for (var x = isClockWise ? A.Count - 1 : 0; isClockWise ? x >= 0 : x < A.Count; x += isClockWise ? -1 : 1)
            {
                var xPart = new StraightTrajectory(A[x]);
                for (var y = isClockWise ? skip : B.Count - 1 - skip; isClockWise ? y < B.Count : y >= 0; y += isClockWise ? 1 : -1)
                {
                    var yPart = new StraightTrajectory(B[y]);
                    var intersect = Intersection.CalculateSingle(xPart, yPart);
                    if (intersect.IsIntersect)
                    {
                        if (isClockWise)
                        {
                            A[x] = xPart.Cut(0f, intersect.FirstT);
                            B[y] = yPart.Cut(intersect.SecondT, 1f);
                            B.RemoveRange(0, y);
                        }
                        else
                        {
                            A[x] = xPart.Cut(intersect.FirstT, 1f);
                            B[y] = yPart.Cut(0f, intersect.SecondT);
                            B.RemoveRange(y + 1, B.Count - (y + 1));
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        public override void GetUIComponents(MarkupFiller filler, List<EditorItem> components, UIComponent parent, bool isTemplate = false)
        {
            base.GetUIComponents(filler, components, parent, isTemplate);
            components.Add(AddElevationProperty(this, parent));

            if (!isTemplate)
            {
                components.Add(AddCornerRadiusProperty(this, parent));
                if (filler.IsMedian)
                    components.Add(AddMedianCornerRadiusProperty(this, parent));
            }
#if DEBUG
            //var material = GetVectorProperty(parent, "Material");
            //var textureA = GetVectorProperty(parent, "TextureA");
            //var textureB = GetVectorProperty(parent, "TextureB");

            //material.Value = (RenderHelper.MaterialLib[MaterialType].GetTexture("_APRMap") as Texture2D).GetPixel(0, 0);
            //textureA.Value = RenderHelper.SurfaceALib[MaterialType].GetPixel(0, 0);
            //textureB.Value = RenderHelper.SurfaceBLib[MaterialType].GetPixel(0, 0);

            //material.OnValueChanged += MaterialValueChanged;
            //textureA.OnValueChanged += TextureAValueChanged;
            //textureB.OnValueChanged += TextureBValueChanged;

            //components.Add(material);
            //components.Add(textureA);
            //components.Add(textureB);

            //void MaterialValueChanged(Color32 value)
            //{
            //    RenderHelper.MaterialLib[MaterialType] = RenderHelper.CreateRoadMaterial(TextureHelper.CreateTexture(128, 128, UnityEngine.Color.white), TextureHelper.CreateTexture(128, 128, value));
            //}
            //void TextureAValueChanged(Color32 value)
            //{
            //    RenderHelper.SurfaceALib[MaterialType] = TextureHelper.CreateTexture(512, 512, value);
            //}
            //void TextureBValueChanged(Color32 value)
            //{
            //    RenderHelper.SurfaceBLib[MaterialType] = TextureHelper.CreateTexture(512, 512, value);
            //}
#endif
        }

        private static FloatPropertyPanel AddElevationProperty(TriangulationFillerStyle triangulationStyle, UIComponent parent)
        {
            var elevationProperty = ComponentPool.Get<FloatPropertyPanel>(parent, nameof(Elevation));
            elevationProperty.Text = Localize.FillerStyle_Elevation;
            elevationProperty.UseWheel = true;
            elevationProperty.WheelStep = 0.1f;
            elevationProperty.WheelTip = Settings.ShowToolTip;
            elevationProperty.CheckMin = true;
            elevationProperty.MinValue = 0f;
            elevationProperty.CheckMax = true;
            elevationProperty.MaxValue = 10f;
            elevationProperty.Init();
            elevationProperty.Value = triangulationStyle.Elevation;
            elevationProperty.OnValueChanged += (float value) => triangulationStyle.Elevation.Value = value;

            return elevationProperty;
        }
        private static FloatPropertyPanel AddCornerRadiusProperty(TriangulationFillerStyle triangulationStyle, UIComponent parent)
        {
            var cornerRadiusProperty = ComponentPool.Get<FloatPropertyPanel>(parent, nameof(CornerRadius));
            cornerRadiusProperty.Text = "Corner radius";
            cornerRadiusProperty.UseWheel = true;
            cornerRadiusProperty.WheelStep = 0.1f;
            cornerRadiusProperty.WheelTip = Settings.ShowToolTip;
            cornerRadiusProperty.CheckMin = true;
            cornerRadiusProperty.MinValue = 0f;
            cornerRadiusProperty.CheckMax = true;
            cornerRadiusProperty.MaxValue = 10f;
            cornerRadiusProperty.Init();
            cornerRadiusProperty.Value = triangulationStyle.CornerRadius;
            cornerRadiusProperty.OnValueChanged += (float value) => triangulationStyle.CornerRadius.Value = value;

            return cornerRadiusProperty;
        }
        private static FloatPropertyPanel AddMedianCornerRadiusProperty(TriangulationFillerStyle triangulationStyle, UIComponent parent)
        {
            var cornerRadiusProperty = ComponentPool.Get<FloatPropertyPanel>(parent, nameof(MedianCornerRadius));
            cornerRadiusProperty.Text = "Corner radius on medians";
            cornerRadiusProperty.UseWheel = true;
            cornerRadiusProperty.WheelStep = 0.1f;
            cornerRadiusProperty.WheelTip = Settings.ShowToolTip;
            cornerRadiusProperty.CheckMin = true;
            cornerRadiusProperty.MinValue = 0f;
            cornerRadiusProperty.CheckMax = true;
            cornerRadiusProperty.MaxValue = 10f;
            cornerRadiusProperty.Init();
            cornerRadiusProperty.Value = triangulationStyle.MedianCornerRadius;
            cornerRadiusProperty.OnValueChanged += (float value) => triangulationStyle.MedianCornerRadius.Value = value;

            return cornerRadiusProperty;
        }
        private ColorPropertyPanel GetVectorProperty(UIComponent parent, string name)
        {
            var vector = ComponentPool.Get<ColorPropertyPanel>(parent);
            vector.Init();
            vector.Text = name;
            return vector;
        }

        public override XElement ToXml()
        {
            var config = base.ToXml();
            Elevation.ToXml(config);
            CornerRadius.ToXml(config);
            MedianCornerRadius.ToXml(config);
            return config;
        }
        public override void FromXml(XElement config, ObjectsMap map, bool invert)
        {
            base.FromXml(config, map, invert);
            Elevation.FromXml(config, DefaultElevation);
            CornerRadius.FromXml(config, DefaultCornerRadius);
            MedianCornerRadius.FromXml(config, DefaultCornerRadius);
        }
    }
    public class PavementFillerStyle : TriangulationFillerStyle
    {
        public override StyleType Type => StyleType.FillerPavement;
        protected override MaterialType MaterialType => MaterialType.Pavement;

        public PavementFillerStyle(Color32 color, float width, float lineOffset, float medianOffset, float elevation, float cornerRadius, float medianCornerRadius) : base(color, width, lineOffset, medianOffset, elevation, cornerRadius, medianCornerRadius) { }

        public override FillerStyle CopyStyle() => new PavementFillerStyle(Color, Width, LineOffset, DefaultOffset, Elevation, CornerRadius, DefaultCornerRadius);
    }
    public class GrassFillerStyle : TriangulationFillerStyle
    {
        public override StyleType Type => StyleType.FillerGrass;
        protected override MaterialType MaterialType => MaterialType.Grass;

        public GrassFillerStyle(Color32 color, float width, float lineOffset, float medianOffset, float elevation, float cornerRadius, float medianCornerRadius) : base(color, width, lineOffset, medianOffset, elevation, cornerRadius, medianCornerRadius) { }

        public override FillerStyle CopyStyle() => new GrassFillerStyle(Color, Width, LineOffset, DefaultOffset, Elevation, CornerRadius, DefaultCornerRadius);
    }
    public class GravelFillerStyle : TriangulationFillerStyle
    {
        public override StyleType Type => StyleType.FillerGravel;
        protected override MaterialType MaterialType => MaterialType.Gravel;

        public GravelFillerStyle(Color32 color, float width, float lineOffset, float medianOffset, float elevation, float cornerRadius, float medianCornerRadius) : base(color, width, lineOffset, medianOffset, elevation, cornerRadius, medianCornerRadius) { }

        public override FillerStyle CopyStyle() => new GravelFillerStyle(Color, Width, LineOffset, DefaultOffset, Elevation, CornerRadius, DefaultCornerRadius);
    }
    public class RuinedFillerStyle : TriangulationFillerStyle
    {
        public override StyleType Type => StyleType.FillerRuined;
        protected override MaterialType MaterialType => MaterialType.Ruined;

        public RuinedFillerStyle(Color32 color, float width, float lineOffset, float medianOffset, float elevation, float cornerRadius, float medianCornerRadius) : base(color, width, lineOffset, medianOffset, elevation, cornerRadius, medianCornerRadius) { }

        public override FillerStyle CopyStyle() => new RuinedFillerStyle(Color, Width, LineOffset, DefaultOffset, Elevation, CornerRadius, DefaultCornerRadius);
    }
    public class CliffFillerStyle : TriangulationFillerStyle
    {
        public override StyleType Type => StyleType.FillerCliff;
        protected override MaterialType MaterialType => MaterialType.Cliff;

        public CliffFillerStyle(Color32 color, float width, float lineOffset, float medianOffset, float elevation, float cornerRadius, float medianCornerRadius) : base(color, width, lineOffset, medianOffset, elevation, cornerRadius, medianCornerRadius) { }

        public override FillerStyle CopyStyle() => new CliffFillerStyle(Color, Width, LineOffset, DefaultOffset, Elevation, CornerRadius, DefaultCornerRadius);
    }
}
