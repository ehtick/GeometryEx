﻿using System;
using System.Collections.Generic;
using ClipperLib;
using System.Linq;
using Elements.Geometry;

namespace GeometryEx
{
    /// <summary>
    /// Utilities for creating and editing Polygons.
    /// </summary>
    public static class Shaper
    {
        /// <summary>
        /// Mode to apply a boolean operation
        /// </summary>
        public enum BooleanMode
        {
            /// <summary>
            /// A and not B
            /// </summary>
            Difference,
            /// <summary>
            /// A or B
            /// </summary>
            Union,
            /// <summary>
            /// A and B
            /// </summary>
            Intersection,
            /// <summary>
            /// Exclusive or — either A or B but not both. 
            /// </summary>
            XOr
        }

        /// <summary>
        /// Creates a rectilinear Polygon in the specified adjacent quadrant to the supplied Polygon's bounding box.
        /// </summary>    
        /// <param name="area">The area of the new Polygon.</param>
        /// <param name="orient">The relative cardinal direction in which the new Polygon will be placed.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon AdjacentArea(Polygon polygon, double area, Orient orient)
        {
            if (polygon == null)
            {
                return null;
            }
            var box = new CompassBox(polygon);
            double sizeX = 0.0;
            double sizeY = 0.0;
            if (orient == Orient.N || orient == Orient.S)
            {
                sizeX = box.SizeX;
                sizeY = area / box.SizeX;
            }
            else
            {
                sizeX = area / box.SizeY;
                sizeY = box.SizeY;
            }
            Vector3 origin = Vector3.Origin;
            switch (orient)
            {
                case Orient.N:
                    origin = box.NW;
                    break;
                case Orient.E:
                    origin = box.SE;
                    break;
                case Orient.S:
                    origin = new Vector3(box.SW.X, box.SW.Y - sizeY);
                    break;
                case Orient.W:
                    origin = new Vector3(box.SW.X - sizeX, box.SW.Y);
                    break;
            }
            return
                new Polygon
                (
                    new[]
                    {
                        origin,
                        new Vector3(origin.X + sizeX, origin.Y),
                        new Vector3(origin.X + sizeX, origin.Y + sizeY),
                        new Vector3(origin.X, origin.Y + sizeY)
                    }
                );
        }

        /// <summary>
        /// Hypothesizes a centerline of a rectangular Polygon by finding the midpoint of the shortest side and creating a line between its midpoint and midpoint of the second segment away from that side.
        /// </summary>
        /// <returns>
        /// A new Line.
        /// </returns>
        public static Line AxisQuad(Polygon polygon)
        {
            if (polygon == null)
            {
                return null;
            }
            var segments = polygon.Segments();
            if (segments.Count() != 4)
            {
                throw new ArgumentException("Polygon must have 4 sides");
            }
            var shortest = segments.OrderBy(s => s.Length()).ToArray()[0];
            var points = polygon.Vertices.ToList();
            points.Remove(shortest.Start);
            points.Remove(shortest.End);
            return new Line(shortest.Midpoint(), new Line(points.First(), points.Last()).Midpoint());
        }

        /// <summary>
        /// Returns the List of Polygons that can merge with the supplied polygon.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="polygons"></param>
        /// <returns></returns>
        public static List<Polygon> CanMerge(Polygon polygon, List<Polygon> polygons)
        {
            var mrgPolygons = new List<Polygon>();
            foreach (var poly in polygons)
            {
                if (Merge(new List<Polygon>() { polygon, poly }).Count == 1)
                {
                    mrgPolygons.Add(poly);
                }
            }
            return mrgPolygons;
        }

        /// <summary>
        /// Apply a boolean operation (Union, Difference, Intersection, or XOr) to two lists of Polygons.
        /// </summary>
        /// <param name="subjectPolygons">Polygons to clip</param>
        /// <param name="clippingPolygons">Polygons with which to clip</param>
        /// <param name="mode">The operation to apply: Union, Difference, Intersection, or XOr</param>
        /// <returns></returns>
        public static IList<Polygon> CombinePolygons(IList<Polygon> subjectPolygons, IList<Polygon> clippingPolygons, BooleanMode mode)
        {
            var subjectPaths = subjectPolygons.Select(s => s.ToClipperPath()).ToList();
            var clipPaths = clippingPolygons.Select(s => s.ToClipperPath()).ToList();
            Clipper clipper = new Clipper();
            clipper.AddPaths(subjectPaths, PolyType.ptSubject, true);
            clipper.AddPaths(clipPaths, PolyType.ptClip, true);
            var solution = new List<List<IntPoint>>();
            var executionMode = ClipType.ctDifference;
            switch (mode)
            {
                case BooleanMode.Difference:
                    executionMode = ClipType.ctDifference;
                    break;
                case BooleanMode.Union:
                    executionMode = ClipType.ctUnion;
                    break;
                case BooleanMode.Intersection:
                    executionMode = ClipType.ctIntersection;
                    break;
                case BooleanMode.XOr:
                    executionMode = ClipType.ctXor;
                    break;
            }
            clipper.Execute(executionMode, solution);
            var polygons = new List<Polygon>();
            if (solution.Count == 0)
            {
                return polygons;
            }
            foreach (List<IntPoint> path in solution)
            {
                polygons.Add(ToPolygon(path));
            }
            return polygons;
        }

        /// <summary>
        /// Creates a convex hull Polygon from the vertices of all supplied Polygons.
        /// </summary>
        /// <param name="polygons">A list of Polygons from which to extract vertices.</param>
        /// <returns></returns>
        public static Polygon ConvexHullFromPolygons(List<Polygon> polygons)
        {
            var points = new List<Vector3>();
            foreach (var polygon in polygons)
            {
                points.AddRange(polygon.Vertices);
            }
            return new Polygon(ConvexHull.MakeHull(points));
        }

        /// <summary>
        /// Constructs the largest geometric difference between this Polygon and the supplied Polygons.
        /// </summary>
        /// <param name="difPolygons">The list of intersecting Polygons.</param>
        /// <returns>
        /// Returns a Polygon representing the subtraction of the supplied Polygons from this Polygon or null if the area of this Polygon is entirely subtracted.
        /// </returns>
        public static Polygon Difference(Polygon polygon, IList<Polygon> difPolys)
        {
            if (polygon == null)
            {
                return null;
            }
            foreach (Polygon differ in difPolys)
            {
                var thisPath = polygon.ToClipperPath();
                var clipper = new Clipper();
                clipper.AddPath(thisPath, PolyType.ptSubject, true);
                clipper.AddPath(differ.ToClipperPath(), PolyType.ptClip, true);
                var solution = new List<List<IntPoint>>();
                clipper.Execute(ClipType.ctDifference, solution);
                if (solution.Count == 0)
                {
                    // polygon has disappeared into a larger polygon.
                    return null;
                }
                var polygons = new List<Polygon>();
                foreach (List<IntPoint> path in solution)
                {
                    polygon = Shaper.ToPolygon(path);
                    if (polygon == null)
                    {
                        continue;
                    }
                    polygons.Add(polygon);
                }
                if (polygons.Count == 0)
                {
                    return null;
                }
                polygon = polygons.OrderByDescending(p => Math.Abs(p.Area())).First();
            }
            if (polygon.IsClockWise())
            {
                return polygon.Reversed();
            }
            return polygon;
        }

        /// <summary>
        /// Constructs the geometric differences between this Polygon and the supplied Polygon.
        /// </summary>
        /// <param name="diff">The intersecting Polygon.</param>
        /// <returns>
        /// Returns a Polygon representing the largest subtraction of the supplied Polygons from this Polygon or null if the area of this Polygon is entirely subtracted.
        /// </returns>
        public static Polygon Difference(Polygon polygon, Polygon diff)
        {
            if (polygon == null || diff == null)
            {
                return null;
            }
            var thisPath = polygon.ToClipperPath();
            var clipper = new Clipper();
            clipper.AddPath(thisPath, PolyType.ptSubject, true);
            clipper.AddPath(diff.ToClipperPath(), PolyType.ptClip, true);
            var solution = new List<List<IntPoint>>();
            clipper.Execute(ClipType.ctDifference, solution);
            if (solution.Count == 0)
            {
                // polygon has disappeared into a larger polygon.
                return null;
            }
            var polygons = new List<Polygon>();
            foreach (List<IntPoint> path in solution)
            {
                polygon = ToPolygon(path);
                if (polygon == null)
                {
                    continue;
                }
                if (polygon.IsClockWise())
                {
                    polygon = polygon.Reversed();
                }
                polygons.Add(polygon);
            }
            return polygons.OrderByDescending(p => Math.Abs(p.Area())).ToList().First();
        }


        /// <summary>
        /// Constructs the geometric differences between this Polygon and the supplied Polygons.
        /// </summary>
        /// <param name="difPolys">The list of intersecting Polygons.</param>
        /// <returns>
        /// Returns a list of Polygons representing the subtraction of the supplied Polygons from this Polygon or null if the area of this Polygon is entirely subtracted.
        /// </returns>
        public static List<Polygon> Differences(Polygon polygon, IList<Polygon> difPolygons)
        {
            var polygons = new List<Polygon>();
            if (polygon == null)
            {
                return polygons;
            }
            foreach (Polygon differ in difPolygons)
            {
                if (polygon == null || differ == null)
                {
                    continue;
                }
                var thisPath = polygon.ToClipperPath();
                var clipper = new Clipper();
                clipper.AddPath(thisPath, PolyType.ptSubject, true);
                clipper.AddPath(differ.ToClipperPath(), PolyType.ptClip, true);
                var solution = new List<List<IntPoint>>();
                clipper.Execute(ClipType.ctDifference, solution);
                if (solution.Count == 0)
                {
                    // polygon has disappeared into a larger polygon.
                    return polygons;
                }
                foreach (List<IntPoint> path in solution)
                {
                    polygon = ToPolygon(path);
                    if (polygon == null)
                    {
                        continue;
                    }
                    if (polygon.IsClockWise())
                    {
                        polygon = polygon.Reversed();
                    }
                    polygons.Add(polygon);
                }
            }
            return polygons.OrderByDescending(p => Math.Abs(p.Area())).ToList();
        }

        /// <summary>
        /// Creates a list of Polygons fitted within a supplied intersecting perimeter.
        /// </summary>
        /// <param name="polygon">This Polygon.</param>
        /// <param name="within">Polygon acting as a constraining outer boundary.</param>
        /// <returns>
        /// A Polygon.
        /// </returns>
        public static List<Polygon> FitWithin(Polygon polygon, Polygon within)
        {
            var intersects = new List<Polygon>();
            if (!within.Intersects(polygon))
            {
                return intersects;
            }
            intersects = within.Intersection(polygon).ToList();
            if (intersects.Count == 0)
            {
                return intersects;
            }
            var polygons = new List<Polygon>();
            foreach (var intersect in intersects)
            {
                if (intersect.IsClockWise())
                {
                    polygons.Add(intersect.Reversed());
                    continue;
                }
                polygons.Add(intersect);
            }
            return polygons;
        }

        /// <summary>
        /// Returns the List of Polygons in the specified coordinate system quadrant.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="polygons"></param>
        /// <returns></returns>
        public static List<Polygon> InQuadrant(List<Polygon> polygons, Quadrant quad)
        {
            var quadPolygons = new List<Polygon>();
            foreach (var polygon in polygons)
            {
                var inQuad = true;
                foreach (var vertex in polygon.Vertices)
                {
                    if (quad == Quadrant.I && (vertex.X < 0.0 || vertex.Y < 0.0))
                    {
                        inQuad = false;
                        break;
                    }
                    if (quad == Quadrant.II && (vertex.X > 0.0 || vertex.Y < 0.0))
                    {
                        inQuad = false;
                        break;
                    }
                    if (quad == Quadrant.III && (vertex.X > 0.0 || vertex.Y > 0.0))
                    {
                        inQuad = false;
                        break;
                    }
                    if (quad == Quadrant.IV && (vertex.X < 0.0 || vertex.Y > 0.0))
                    {
                        inQuad = false;
                        break;
                    }
                }
                if (inQuad)
                {
                    quadPolygons.Add(polygon);
                }
            }
            return quadPolygons;
        }

        /// <summary>
        /// Constructs a list of line segments in order from pairs in a list of vertices.
        /// </summary>
        /// <param name="vertices">List of vertices to convert to line segments.</param>
        /// <returns>List of Lines.</returns>
        public static List<Line> LinesFromPoints(List<Vector3> points)
        {
            var lines = new List<Line>();
            if (points.Count == 0)
            {
                return lines;
            }
            for (var i = 0; i < points.Count - 1; i++)
            {
                lines.Add(new Line(points[i], points[i + 1]));
            }
            return lines;
        }

        /// <summary>
        /// Constructs the geometric union of the supplied list of Polygons.
        /// </summary>
        /// <param name="polygons">The list of Polygons to be combined.</param>
        /// <returns>
        /// List of Polygons.
        /// </returns>
        public enum FillType { EvenOdd, NonZero, Positive, Negative };
        public static List<Polygon> Merge(List<Polygon> polygons, FillType fillType = FillType.NonZero)
        {
            if (polygons.Count == 0)
            {
                return polygons;
            }
            var filtyp = (PolyFillType) fillType;
            var polyPaths = new List<List<IntPoint>>();
            foreach (Polygon polygon in polygons)
            {
                polyPaths.Add(polygon.ToClipperPath());
            }
            Clipper clipper = new Clipper();
            clipper.AddPaths(polyPaths, PolyType.ptClip, true);
            clipper.AddPaths(polyPaths, PolyType.ptSubject, true);
            var solution = new List<List<IntPoint>>();
            clipper.Execute(ClipType.ctUnion, solution, filtyp);
            if (solution.Count == 0)
            {
                return polygons;
            }
            var mergePolygons = new List<Polygon>();
            foreach (var solved in solution)
            {
                var polygon = solved.ToPolygon();
                if (polygon.IsClockWise())
                {
                    polygon = polygon.Reversed();
                }
                mergePolygons.Add(polygon);
            }
            return mergePolygons;
        }

        /// <summary>
        /// Returns the List of Polygons that do not intersect the supplied polygon.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="polygons"></param>
        /// <returns></returns>
        public static List<Polygon> NonIntersecting(Polygon polygon, List<Polygon> polygons)
        {
            var nonPolygons = new List<Polygon>();
            foreach (var poly in polygons)
            {
                if (!polygon.Intersects(poly))
                {
                    nonPolygons.Add(poly);
                }
            }
            return nonPolygons;
        }

        /// <summary>
        /// Returns the List of Polygons that do not intersect the supplied polygons.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="polygons"></param>
        /// <returns></returns>
        public static List<Polygon> NonIntersecting(List<Polygon> placed, List<Polygon> polygons)
        {
            var nonPolygons = new List<Polygon>();
            foreach (var polygon in polygons)
            {
                if (polygon.Intersects(placed))
                {
                    continue;
                }
                nonPolygons.Add(polygon);
            }
            return nonPolygons;
        }

        /// <summary>
        /// Constructs the set of nearby Polygons from 8 bounding boxes (as delivered by the nearPolygon and its orthogonal) at each vertex of polygon.
        /// </summary>
        /// <param name="polygon"></param>
        /// <param name="nearPolygon"></param>
        /// <returns></returns>
        public static List<Polygon> NearPolygons(Polygon polygon, Polygon nearPolygon, bool rotated = false) 
        {
            var polygons = new List<Polygon>();
            var points = polygon.Vertices;
            var compass = nearPolygon.Compass();
            foreach (var point in points)
            {
                polygons.Add(nearPolygon.MoveFromTo(compass.SW, point));
                polygons.Add(nearPolygon.MoveFromTo(compass.SE, point));
                polygons.Add(nearPolygon.MoveFromTo(compass.NE, point));
                polygons.Add(nearPolygon.MoveFromTo(compass.NW, point));
            }
            if (!rotated)
            {
                return polygons;
            }
            compass = nearPolygon.Rotate(Vector3.Origin, 90.0).Compass();
            foreach (var point in points)
            {
                polygons.Add(nearPolygon.MoveFromTo(compass.SW, point));
                polygons.Add(nearPolygon.MoveFromTo(compass.SE, point));
                polygons.Add(nearPolygon.MoveFromTo(compass.NE, point));
                polygons.Add(nearPolygon.MoveFromTo(compass.NW, point));
            }
            return polygons;
        }

        /// <summary>
        /// Creates a rectangular Polygon of the supplied length to width proportion at the supplied area with its southwest corner at the origin.
        /// </summary>
        /// <param name="area">Required area of the Polygon.</param>
        /// <param name="ratio">Ratio of width to depth.</param>
        /// <param name="moveTo">Location of the southwest corner of the new Polygon.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon RectangleByArea(double area, double ratio = 1.0)
        {
            if (area <= 0.0 || ratio <= 0.0)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            var x = Math.Sqrt(area * ratio);
            var y = area / Math.Sqrt(area * ratio);
            var vertices =
                new[]
                {
                    Vector3.Origin,
                    new Vector3(x, 0.0),
                    new Vector3(x, y),
                    new Vector3(0.0, y)
                };
            return Polygon.Rectangle(Vector3.Origin, new Vector3(x, y));
        }

        /// <summary>
        /// Creates a rectangular Polygon of the supplied length to width proportion at the supplied area with its southwest corner at the origin.
        /// </summary>
        /// <param name="ratio">Ratio of width to depth.</param>
        /// <param name="moveTo">Location of the southwest corner of the new Polygon.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon RectangleByRatio(double ratio = 1.0)
        {
            if (ratio <= 0.0)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            return Polygon.Rectangle(Vector3.Origin, new Vector3(1.0, ratio));
        }

        /// <summary>
        /// Creates an C-shaped Polygon within a specified rectangle with its southwest corner at the origin.
        /// </summary>
        /// <param name="origin">The southwest enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon C(Vector3 origin, Vector3 size, double width)
        {
            if (size.X <= 0 || size.Y <= 0 || width >= size.X || width * 3 >= size.Y)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            return
                new Polygon
                (
                    new[]
                    {
                        Vector3.Origin,
                        new Vector3(size.X, Vector3.Origin.Y),
                        new Vector3(size.X, width),
                        new Vector3(width, width),
                        new Vector3(width, size.Y - width),
                        new Vector3(size.X, size.Y - width),
                        new Vector3(size.X, size.Y),
                        new Vector3(Vector3.Origin.X, size.Y),
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Creates an E-shaped Polygon within a specified rectangle.
        /// </summary>
        /// <param name="origin">The southwest enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon E(Vector3 origin, Vector3 size, double width)
        {
            if (size.X <= 0 || size.Y <= 0 || width >= size.X || width * 3 >= size.Y)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            var halfWidth = width * 0.5;
            var xAxis = size.Y * 0.5;
            return
                new Polygon
                (
                    new[]
                    {
                        Vector3.Origin,
                        new Vector3(size.X, Vector3.Origin.Y),
                        new Vector3(size.X, width),
                        new Vector3(width, width),
                        new Vector3(width, xAxis - halfWidth),//
                        new Vector3(size.X, xAxis - halfWidth),
                        new Vector3(size.X, xAxis + halfWidth),
                        new Vector3(width, xAxis + halfWidth),
                        new Vector3(width, size.Y - width),//
                        new Vector3(size.X, size.Y - width),
                        new Vector3(size.X, size.Y),
                        new Vector3(Vector3.Origin.X, size.Y),
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Creates an F-shaped Polygon within a specified rectangle.
        /// </summary>
        /// <param name="origin">The initial enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon F(Vector3 origin, Vector3 size, double width)
        {
            if (size.X <= 0 || size.Y <= 0 || width >= size.X || width * 2 >= size.Y)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            var halfWidth = width * 0.5;
            var xAxis = size.Y * 0.5;
            return
                new Polygon
                (
                    new[]
                    {
                        Vector3.Origin,
                        new Vector3(width, Vector3.Origin.Y),
                        new Vector3(width, xAxis - halfWidth),
                        new Vector3(size.X, xAxis - halfWidth),
                        new Vector3(size.X, xAxis + halfWidth),
                        new Vector3(width, xAxis + halfWidth),
                        new Vector3(width, size.Y - width),
                        new Vector3(size.X, size.Y - width),
                        new Vector3(size.X, size.Y),
                        new Vector3(Vector3.Origin.X, size.Y),
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Creates an H-shaped Polygon within a specified rectangle.
        /// </summary>
        /// <param name="origin">The initial enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <param name="offset">Positive or negative displacement of the H crossbar from the shape meridian.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon H(Vector3 origin, Vector3 size, double width)
        {
            if (size.X <= 0 || size.Y <= 0 || width * 2 >= size.X || width >= size.Y)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            var halfWidth = width * 0.5;
            var xAxis = size.Y * 0.5;
            var rightWest = size.X - width;
            return
                new Polygon
                (
                    new[]
                    {
                        Vector3.Origin,
                        new Vector3(width, Vector3.Origin.Y),
                        new Vector3(width, xAxis - halfWidth),
                        new Vector3(rightWest, xAxis - halfWidth),
                        new Vector3(rightWest, Vector3.Origin.Y),
                        new Vector3(size.X, Vector3.Origin.Y),
                        new Vector3(size.X, size.Y),
                        new Vector3(rightWest, size.Y),
                        new Vector3(rightWest, xAxis + halfWidth),
                        new Vector3(width, xAxis + halfWidth),
                        new Vector3(width, size.Y),
                        new Vector3(Vector3.Origin.X, size.Y),
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Creates an L-shaped Polygon within a specified rectangle.
        /// </summary>
        /// <param name="origin">The initial enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon L(Vector3 origin, Vector3 size, double width)
        {
            if (size.X <= 0 || size.Y <= 0 || width >= size.X || width >= size.Y)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            return
                new Polygon
                (
                    new[]
                    {
                        Vector3.Origin,
                        new Vector3(size.X, Vector3.Origin.Y),
                        new Vector3(size.X, width),
                        new Vector3(width, width),
                        new Vector3(width, size.Y),
                        new Vector3(Vector3.Origin.X, size.Y)
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Creates a T-shaped Polygon within a specified rectangle.
        /// </summary>
        /// <param name="origin">The initial enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon T(Vector3 origin, Vector3 size, double width)
        {
            if (size.X <= 0 || size.Y <= 0 || width >= size.X || width >= size.Y)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            var halfWidth = width * 0.5;
            var yAxis = origin.X + (size.X * 0.5);
            return
                new Polygon
                (
                    new[]
                    {
                        new Vector3(yAxis - halfWidth, 0),
                        new Vector3(yAxis + halfWidth, 0),
                        new Vector3(yAxis + halfWidth, size.Y - width),
                        new Vector3(size.X, size.Y - width),
                        new Vector3(size.X, size.Y),
                        new Vector3(Vector3.Origin.X, size.Y),
                        new Vector3(Vector3.Origin.X, size.Y - width),
                        new Vector3(yAxis - halfWidth, size.Y - width)
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Creates U-shaped Polygon within a specified rectangle.
        /// </summary>
        /// <param name="origin">The initial enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon U(Vector3 origin, Vector3 size, double width)
        {
            if (size.X <= 0 || size.Y <= 0 || width * 2 >= size.X || width >= size.Y)
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            return
                new Polygon
                (
                    new[]
                    {
                        Vector3.Origin,
                        new Vector3(size.X, Vector3.Origin.Y),
                        new Vector3(size.X, Vector3.Origin.Y + size.Y),
                        new Vector3(size.X - width, Vector3.Origin.Y + size.Y),
                        new Vector3(size.X - width, Vector3.Origin.Y + width),
                        new Vector3(width, Vector3.Origin.Y + width),
                        new Vector3(width, Vector3.Origin.Y + size.Y),
                        new Vector3(Vector3.Origin.X, Vector3.Origin.Y + size.Y)
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Creates an X-shaped Polygon within a specified rectangle.
        /// </summary>
        /// <param name="origin">The initial enclosing box corner.</param>
        /// <param name="size">The positive x and y delta defining the size of the enclosing box.</param>
        /// <param name="width">Width of each stroke of the shape.</param>
        /// <returns>
        /// A new Polygon.
        /// </returns>
        public static Polygon X(Vector3 origin, Vector3 size, double width)
        {
            if (width >= Math.Abs(size.X - origin.X) || width >= Math.Abs(size.Y - origin.Y))
            {
                throw new ArgumentOutOfRangeException(Messages.POLYGON_SHAPE_EXCEPTION);
            }
            var halfWidth = width * 0.5;
            var xAxis = origin.Y + (size.Y * 0.5);
            var yAxis = origin.X + (size.X * 0.5);
            return
                new Polygon
                (
                    new[]
                    {
                        new Vector3(yAxis - halfWidth, Vector3.Origin.Y),
                        new Vector3(yAxis + halfWidth, Vector3.Origin.Y),
                        new Vector3(yAxis + halfWidth, xAxis - halfWidth),
                        new Vector3(size.X, xAxis - halfWidth),
                        new Vector3(size.X, xAxis + halfWidth),
                        new Vector3(yAxis + halfWidth, xAxis + halfWidth),
                        new Vector3(yAxis + halfWidth, size.Y),
                        new Vector3(yAxis - halfWidth, size.Y),
                        new Vector3(yAxis - halfWidth, xAxis + halfWidth),
                        new Vector3(Vector3.Origin.X, xAxis + halfWidth),
                        new Vector3(Vector3.Origin.X, xAxis - halfWidth),
                        new Vector3(yAxis - halfWidth, xAxis - halfWidth)
                    }
                ).MoveFromTo(Vector3.Origin, origin);
        }

        /// <summary>
        /// Check if any of lines have zero length.
        /// </summary>
        public static bool ZeroLength(List<Line> lines)
        {
            if (lines.Count == 0)
            {
                return false;
            }
            foreach (var s in lines)
            {
                if (s.Length() == 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check for self-intersection in the supplied lines.
        /// </summary>
        /// <param name="lines">List of lines to check.</param>
        public static bool SelfIntersects(List<Line> lines)
        {
            if (lines.Count == 0)
            {
                return true;
            }
            for (var i = 0; i < lines.Count; i++)
            {
                for (var j = 0; j < lines.Count; j++)
                {
                    if (i == j)
                    {
                        // Don't check against itself.
                        continue;
                    }
                    if (lines[i].Intersects2D(lines[j]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Tests if two doubles are effectively equal within a tolerance.
        /// </summary>
        /// <param name="thisValue">The lower bound of the random range.</param>
        /// <param name="thatValue">The upper bound of the random range.</param>
        /// <returns>
        /// True if the supplied values are equivalent within the default or supplied tolerance.
        /// </returns>
        public static bool NearEqual(this double thisValue, double thatValue, double tolerance = 1e-9)
        {
            if (Math.Abs(thisValue - thatValue) > tolerance)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns a random double within the supplied range.
        /// </summary>
        /// <param name="minValue">The lower bound of the random range.</param>
        /// <param name="minValue">The upper bound of the random range.</param>
        /// <returns>
        /// A random double within the range.
        /// </returns>
        public static double RandomDouble(double minvalue, double maxvalue)
        {
            var scale = 1000000.0;
            var rnd = new Random();
            double next = rnd.Next((int)Math.Round(minvalue * scale), (int)Math.Round(maxvalue * scale));
            return next / scale;
        }

        public const double scale = 1024.0;

        /// <summary>
        /// Construct a clipper path from a Polygon.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        internal static List<IntPoint> ToClipperPath(this Polygon p)
        {
            var path = new List<IntPoint>();
            foreach (var v in p.Vertices)
            {
                path.Add(new IntPoint(v.X * scale, v.Y * scale));
            }
            return path.Distinct().ToList();
        }

        /// <summary>
        /// Construct a Polygon from a clipper path 
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        internal static Polygon ToPolygon(this List<IntPoint> p)
        {
            var points = p.Select(v => new Vector3(v.X / scale, v.Y / scale)).Distinct().ToList();
            var lines = LinesFromPoints(points);
            if (ZeroLength(lines) || SelfIntersects(lines))
            {
                return null;
            }
            try
            {
                return new Polygon(points);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
