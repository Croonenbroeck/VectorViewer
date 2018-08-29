﻿// XAML Map Control - https://github.com/ClemensFischer/XAML-Map-Control
// © 2018 Clemens Fischer
// Licensed under the Microsoft Public License (Ms-PL)

using System;
using System.Text;
#if !WINDOWS_UWP
using System.Windows;
#endif
using GeoAPI.CoordinateSystems;
using GeoAPI.CoordinateSystems.Transformations;
using ProjNet.Converters.WellKnownText;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace MapControl.Projections
{
    /// <summary>
    /// MapProjection based on ProjNET4GeoApi.
    /// </summary>
    public class GeoApiProjection : MapProjection
    {
        private ICoordinateTransformation coordinateTransform;
        private IMathTransform mathTransform;
        private IMathTransform inverseTransform;

        /// <summary>
        /// Gets or sets the underlying ICoordinateTransformation instance.
        /// Setting this property updates the CrsId property.
        /// </summary>
        public ICoordinateTransformation CoordinateTransform
        {
            get { return coordinateTransform; }
            set
            {
                coordinateTransform = value;
                mathTransform = coordinateTransform.MathTransform;
                inverseTransform = mathTransform.Inverse();

                if (coordinateTransform.TargetCS != null &&
                    !string.IsNullOrEmpty(coordinateTransform.TargetCS.Authority) &&
                    coordinateTransform.TargetCS.AuthorityCode > 0)
                {
                    CrsId = string.Format("{0}:{1}", coordinateTransform.TargetCS.Authority, coordinateTransform.TargetCS.AuthorityCode);
                }
            }
        }

        /// <summary>
        /// Gets or sets an OGC Well-known text representation of a projected coordinate system,
        /// i.e. a PROJCS[...] string as used by https://epsg.io or http://spatialreference.org.
        /// Setting this property updates the CoordinateTransform property.
        /// </summary>
        public string WKT
        {
            get { return coordinateTransform?.TargetCS?.WKT; }
            set
            {
                var sourceCs = GeographicCoordinateSystem.WGS84;
                var targetCs = (ICoordinateSystem)CoordinateSystemWktReader.Parse(value, Encoding.UTF8);

                CoordinateTransform = new CoordinateTransformationFactory().CreateFromCoordinateSystems(sourceCs, targetCs);
            }
        }

        public override Point LocationToPoint(Location location)
        {
            if (mathTransform == null)
            {
                throw new InvalidOperationException("The Wkt property is not set or not valid.");
            }

            var xy = mathTransform.Transform(new double[] { location.Longitude, location.Latitude });

            return new Point(xy[0], xy[1]);
        }

        public override Location PointToLocation(Point point)
        {
            if (inverseTransform == null)
            {
                throw new InvalidOperationException("The Wkt property is not set or not valid.");
            }

            var lonLat = inverseTransform.Transform(new double[] { point.X, point.Y });

            return new Location(lonLat[1], lonLat[0]);
        }
    }
}
