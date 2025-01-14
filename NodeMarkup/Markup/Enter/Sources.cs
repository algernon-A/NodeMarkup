﻿using ModsCommon.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NodeMarkup.Manager
{
    public interface IPointSource
    {
        public MarkupPoint.LocationType Location { get; }
        public NetworkType NetworkType { get; }
        public void GetPositionAndDirection(float offset, out Vector3 position, out Vector3 direction);
    }
    public class NetInfoPointSource : IPointSource
    {
        public Enter Enter { get; }
        private DriveLane LeftLane { get; }
        private DriveLane RightLane { get; }
        public MarkupPoint.LocationType Location { get; private set; }
        public NetworkType NetworkType { get; private set; }

        public bool IsEdge => GetIsEdge(LeftLane, RightLane);
        public float CenterDelte => GetCenterDelte(LeftLane, RightLane);
        public float SideDelta => GetSideDelta(LeftLane, RightLane);

        public NetInfoPointSource(Enter enter, DriveLane leftLane, DriveLane rightLane, MarkupPoint.LocationType location)
        {
            Enter = enter;
            LeftLane = leftLane;
            RightLane = rightLane;
            Location = location;
            NetworkType = (LeftLane == null ? NetworkType.None : leftLane.NetworkType) | (RightLane == null ? NetworkType.None : RightLane.NetworkType);
        }

        public void GetPositionAndDirection(float offset, out Vector3 position, out Vector3 direction)
        {
            if ((Location & MarkupPoint.LocationType.Between) != MarkupPoint.LocationType.None)
                GetMiddlePositionAndDirection(offset, out position, out direction);

            else if ((Location & MarkupPoint.LocationType.Edge) != MarkupPoint.LocationType.None)
                GetEdgePositionAndDirection(Location, offset, out position, out direction);

            else
                throw new Exception();
        }

        private void GetMiddlePositionAndDirection(float offset, out Vector3 position, out Vector3 direction)
        {
            if (RightLane == LeftLane)
            {
                RightLane.NetLane.CalculatePositionAndDirection(Enter.T, out position, out direction);
                direction = direction.normalized * Enter.SideSign;
            }
            else
            {
                RightLane.NetLane.CalculatePositionAndDirection(Enter.T, out Vector3 rightPos, out Vector3 rightDir);
                LeftLane.NetLane.CalculatePositionAndDirection(Enter.T, out Vector3 leftPos, out Vector3 leftDir);

                direction = ((rightDir + leftDir) / (Enter.SideSign * 2)).normalized;

                var part = (RightLane.HalfWidth + SideDelta / 2) / CenterDelte;
                position = Vector3.Lerp(rightPos, leftPos, part);
            }

            position += Enter.CornerDir * (offset / Enter.TranformCoef);
        }
        private void GetEdgePositionAndDirection(MarkupPoint.LocationType location, float offset, out Vector3 position, out Vector3 direction)
        {
            float lineShift;
            switch (location)
            {
                case MarkupPoint.LocationType.LeftEdge:
                    RightLane.NetLane.CalculatePositionAndDirection(Enter.T, out position, out direction);
                    lineShift = -RightLane.HalfWidth;
                    break;
                case MarkupPoint.LocationType.RightEdge:
                    LeftLane.NetLane.CalculatePositionAndDirection(Enter.T, out position, out direction);
                    lineShift = LeftLane.HalfWidth;
                    break;
                default:
                    throw new Exception();
            }
            direction = (direction * Enter.SideSign).normalized;

            var shift = (lineShift + offset) / Enter.TranformCoef;

            position += Enter.CornerDir * shift;
        }

        public static IEnumerable<NetInfoPointSource> GetSource(Enter enter, DriveLane leftLane, DriveLane rightLane)
        {
            if (GetIsEdge(leftLane, rightLane))
            {
                yield return new NetInfoPointSource(enter, leftLane, rightLane, rightLane == null ? MarkupPoint.LocationType.RightEdge : MarkupPoint.LocationType.LeftEdge);
            }
            else if (leftLane == rightLane)
            {
                yield return new NetInfoPointSource(enter, leftLane, rightLane, MarkupPoint.LocationType.Between);
            }
            else if (GetSideDelta(leftLane, rightLane) >= (leftLane.HalfWidth + rightLane.HalfWidth) / 2)
            {
                yield return new NetInfoPointSource(enter, leftLane, rightLane, MarkupPoint.LocationType.RightEdge);
                yield return new NetInfoPointSource(enter, leftLane, rightLane, MarkupPoint.LocationType.LeftEdge);
            }
            else
            {
                yield return new NetInfoPointSource(enter, leftLane, rightLane, MarkupPoint.LocationType.Between);
            }
        }
        public static bool GetIsEdge(DriveLane leftLane, DriveLane rightLane) => rightLane == null ^ leftLane == null;
        public static float GetSideDelta(DriveLane leftLane, DriveLane rightLane) => GetIsEdge(leftLane, rightLane) ? 0f : Mathf.Abs(rightLane.LeftSidePos - leftLane.RightSidePos);
        public static float GetCenterDelte(DriveLane leftLane, DriveLane rightLane) => GetIsEdge(leftLane, rightLane) ? 0f : Mathf.Abs(rightLane.Position - leftLane.Position);
    }


    public class RoadGeneratorPointSource : IPointSource
    {
        public Enter Enter { get; }
        public float Position { get; }
        public float Height { get; }
        public MarkupPoint.LocationType Location => throw new NotImplementedException();
        public NetworkType NetworkType => throw new NotImplementedException();

        public RoadGeneratorPointSource(Enter enter, float position, float height = -0.3f)
        {
            Enter = enter;
            Position = position;
            Height = height;
        }

        public void GetPositionAndDirection(float offset, out Vector3 position, out Vector3 direction)
        {
            position = Enter.GetPosition(Position + offset) + new Vector3(0f, Height, 0f);
            direction = Enter.NormalDir;
        }
    }
    public class DriveLane
    {
        private Enter Enter { get; }

        public uint LaneId { get; }
        public NetLane NetLane => LaneId.GetLane();
        public NetworkType NetworkType { get; }

        public float Position { get; }
        public float HalfWidth { get; }
        public float LeftSidePos => Position + (Enter.IsLaneInvert ? -HalfWidth : HalfWidth);
        public float RightSidePos => Position + (Enter.IsLaneInvert ? HalfWidth : -HalfWidth);

        public DriveLane(Enter enter, uint laneId, NetInfo.Lane info, NetworkType type)
        {
            Enter = enter;
            LaneId = laneId;
            Position = info.m_position;
            HalfWidth = Mathf.Abs(info.m_width) / 2;
            NetworkType = type;
        }

        public override string ToString() => LaneId.ToString();
    }
    [Flags]
    public enum NetworkType
    {
        None = 0,
        Road = 1 << 0,
        Track = 1 << 1,
        Taxiway = 1 << 2,
        Path = 1 << 3,

        All = Road | Track | Taxiway | Path,
    }
    [AttributeUsage(AttributeTargets.Field)]
    public class NetworkTypeAttribute : Attribute
    {
        public NetworkType Type { get; }

        public NetworkTypeAttribute(NetworkType type)
        {
            Type = type;
        }
    }
}
