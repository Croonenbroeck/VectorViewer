using MapControl;
using MapControl.Caching;
using MapControl.UiTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

/*

TODO:
- Viewport NACH Scrollen herausfinden.
- Douglas-Peucker-Algorithmus parameterlos? Alternativ: Bestimme Toleranz über Viewport-Größe (siehe https://github.com/ClemensFischer/XAML-Map-Control/issues/70).
- Rasterdaten: Siehe https://github.com/ClemensFischer/XAML-Map-Control/issues/45

*/

namespace SampleApplication
{
    public class GeomInfos
    {
        // ----------- Fields.

        private List<List<Point>> _GeoPoints;
        public List<string> GeoStrings { get; set; }
        public double[] BBox;
        public GeomType GeoType;
        public VectorData vec;

        // ----------- Getters and setters.

        public List<List<Point>> GeomPoints
        {
            get { return (_GeoPoints); }
            set
            {
                _GeoPoints = value;
                GeoStrings = Stringify(value);
                BBox = GetBBox();
            }
        }

        // ----------- Constructors.

        // Parameterless constructor.
        public GeomInfos()
        {
            // Nothing much to do here...
        }

        // Constructor that accepts a list of points lists.
        public GeomInfos(List<List<Point>> PointsLists)
        {
            GeomPoints = PointsLists; // Note that this invokes the setter, which itself invokes other things...
        }

        // Constructor that accepts a VectorData instance.
        public GeomInfos(VectorData vecData)
        {
            vec = vecData;

            string geometryType = vecData.FeatureCollection[0].Geometry.GeometryType;
            switch (geometryType)
            {
                case "Point" or "MultiPoint":
                    GeoType = GeomType.Point;

                    break;
                case "LineString" or "Polygon" or "MultiPolygon":
                    GeoType = GeomType.Polygon;

                    break;
            }

            List<List<Point>> FeaturePointsList = new List<List<Point>>();

            foreach (NetTopologySuite.Features.IFeature f in vecData.FeatureCollection)
            {
                List<Point> ThesePoints = new List<Point>();

                foreach (NetTopologySuite.Geometries.Coordinate c in f.Geometry.Coordinates)
                {
                    Point MyPoint = new Point();
                    MyPoint.X = c[0];
                    MyPoint.Y = c[1];
                    ThesePoints.Add(MyPoint);
                }
                FeaturePointsList.Add(ThesePoints);
            }

            GeomPoints = FeaturePointsList; // Note that this invokes the setter, which itself invokes other things...
        }

        // ----------- Private methods.

        private List<string> Stringify(List<List<Point>> PointsList)
        {
            int i;
            string[] NewStrParts;
            List<string> GeomStrings = new List<string>();

            foreach (List<Point> l in PointsList)
            {
                NewStrParts = new string[l.Count];

                i = 0;
                foreach (Point p in l)
                {
                    NewStrParts[i] = p.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + p.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    i++;
                }

                GeomStrings.Add(String.Join(" ", NewStrParts));
            }

            return (GeomStrings);
        }

        private double[] GetBBox()
        {
            double MinLon = 999;
            double MinLat = 999;
            double MaxLon = -999;
            double MaxLat = -999;

            foreach (List<Point> l in _GeoPoints)
            {
                foreach (Point p in l)
                {
                    if (p.X < MinLat) MinLat = p.X;
                    if (p.X > MaxLat) MaxLat = p.X;
                    if (p.Y < MinLon) MinLon = p.Y;
                    if (p.Y > MaxLon) MaxLon = p.Y;
                }
            }

            double[] MyBBox = new double[4];
            MyBBox[0] = MinLon;
            MyBBox[1] = MaxLon;
            MyBBox[2] = MinLat;
            MyBBox[3] = MaxLat;

            return (MyBBox);
        }
    }

    // -------------------------------------------------------------------------------

    public partial class MainWindow : Window
    {
        public GeomInfos MyGeomInfos;

        static MainWindow()
        {
            ImageLoader.HttpClient.DefaultRequestHeaders.Add("User-Agent", "XAML Map Control Test Application");

            TileImageLoader.Cache = new ImageFileCache(TileImageLoader.DefaultCacheFolder);
        }

        public MainWindow()
        {
            InitializeComponent();

            AddChartServerLayer();

            if (TileImageLoader.Cache is ImageFileCache cache)
            {
                Loaded += async (s, e) =>
                {
                    await Task.Delay(2000);
                    await cache.Clean();
                };
            }
        }

        private void CutAndSimplify(MouseWheelEventArgs e = null, BoundingBox bbox = null)
        {
            if (MyGeomInfos == null) return;

            double Tolerance = 0.000001;

            if (bbox == null) bbox = map.ViewRectToBoundingBox(new Rect(0, 0, map.ActualWidth, map.ActualHeight));

            NetTopologySuite.Features.FeatureCollection InFC = MyGeomInfos.vec.FeatureCollection;
            NetTopologySuite.Features.FeatureCollection OutFC = new NetTopologySuite.Features.FeatureCollection();
            foreach (NetTopologySuite.Features.IFeature f in InFC)
            {
                foreach (NetTopologySuite.Geometries.Coordinate c in f.Geometry.Coordinates)
                {
                    if (c.X >= bbox.West & c.X <= bbox.East & c.Y >= bbox.South & c.Y <= bbox.North) // If any point is within the bounding box, use this feature.
                    {
                        OutFC.Add(f);
                        break;
                    }
                }
            }
            byte[] NewBinary = FlatGeobuf.NTS.FeatureCollectionConversions.Serialize(OutFC, FlatGeobuf.GeometryType.Unknown);
            VectorData CurrentViewData = new VectorData(NewBinary);
            GeomInfos CurrentViewGeom = new GeomInfos(CurrentViewData);

            if (e != null)
            {
                switch (map.ZoomLevel + Math.Sign(e.Delta))
                {
                    case <= 2:
                        Tolerance = 0.02;
                        break;
                    case > 2 and <= 3:
                        Tolerance = 0.02;
                        break;
                    case > 3 and <= 4:
                        Tolerance = 0.02;
                        break;
                    case > 4 and <= 5:
                        Tolerance = 0.02;
                        break;
                    case > 5 and <= 6:
                        Tolerance = 0.02;
                        break;
                    case > 6 and <= 7:
                        Tolerance = 0.02;
                        break;
                    case > 7 and <= 8:
                        Tolerance = 0.005;
                        break;
                    case > 8 and <= 9:
                        Tolerance = 0.005;
                        break;
                    case > 9 and <= 10:
                        Tolerance = 0.001;
                        break;
                    case > 10 and <= 11:
                        Tolerance = 0.0005;
                        break;
                    case > 11 and <= 12:
                        Tolerance = 0.0005;
                        break;
                    case > 12 and <= 13:
                        Tolerance = 0.0001;
                        break;
                    case > 13 and <= 14:
                        Tolerance = 0.00004;
                        break;
                    case > 14 and <= 15:
                        Tolerance = 0.00002;
                        break;
                    case > 15 and <= 16:
                        Tolerance = 0.00001;
                        break;
                    case > 16 and <= 17:
                        Tolerance = 0.000005;
                        break;
                    case > 17 and <= 18:
                        Tolerance = 0.000004;
                        break;
                    case > 18 and <= 19:
                        Tolerance = 0.000003;
                        break;
                    case > 19 and <= 20:
                        Tolerance = 0.000002;
                        break;
                    case >= 20:
                        Tolerance = 0.000001;
                        break;
                }
            }

            List<List<Point>> ZoomPoints = new List<List<Point>>();
            foreach (List<Point> p in CurrentViewGeom.GeomPoints) { ZoomPoints.Add(Douglas_Peucker.DouglasPeuckerReduction(p, Tolerance)); }

            GeomInfos ZoomGeom = new GeomInfos(ZoomPoints);

            MapViewModel NewMapDrawings = new MapViewModel(ZoomGeom.GeoStrings, MyGeomInfos.GeoType);
            DataContext = null;
            DataContext = NewMapDrawings;
        }

        partial void AddChartServerLayer();

        private void ResetHeadingButtonClick(object sender, RoutedEventArgs e)
        {
            map.TargetHeading = 0d;
        }

        private void MapMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                //map.ZoomMap(e.GetPosition(map), Math.Floor(map.ZoomLevel + 1.5));
                //map.ZoomToBounds(new BoundingBox(53, 7, 54, 9));
                map.TargetCenter = map.ViewToLocation(e.GetPosition(map));
            }
        }

        private void MapMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "FlatGeobuf files (*.fgb)|*.fgb|" +
                                    "Shapefiles (*.shp)|*.shp|" +
                                    "All files (*.*)|*.*";
            openFileDialog.FilterIndex = openFileDialog.Filter.Length;
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Title = "Open a vector data file...";

            Nullable<bool> result = openFileDialog.ShowDialog();
            if (result != true) return;

            VectorData vecData = new VectorData(openFileDialog.FileName);
            vecData.TransformToWGS84();
            MapViewModel NewMapDrawings;
            BoundingBox bbox;

            string geometryType = vecData.FeatureCollection[0].Geometry.GeometryType;
            switch (geometryType)
            {
                case "Point" or "MultiPoint":
                    Mouse.OverrideCursor = Cursors.Wait;
                    MyGeomInfos = new GeomInfos(vecData);
                    bbox = new BoundingBox(MyGeomInfos.BBox[0], MyGeomInfos.BBox[2], MyGeomInfos.BBox[1], MyGeomInfos.BBox[3]);
                    map.ZoomToBounds(bbox);
                    CutAndSimplify(bbox: bbox);
                    NewMapDrawings = new MapViewModel(MyGeomInfos.GeoStrings, MyGeomInfos.GeoType);
                    DataContext = NewMapDrawings;
                    Mouse.OverrideCursor = Cursors.Arrow;                
                    break;
                case "LineString" or "Polygon" or "MultiPolygon":
                    Mouse.OverrideCursor = Cursors.Wait;
                    System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
                    watch.Start();
                    MyGeomInfos = new GeomInfos(vecData);
                    bbox = new BoundingBox(MyGeomInfos.BBox[0], MyGeomInfos.BBox[2], MyGeomInfos.BBox[1], MyGeomInfos.BBox[3]);
                    map.ZoomToBounds(bbox);
                    CutAndSimplify(bbox: bbox);
                    Mouse.OverrideCursor = Cursors.Arrow;
                    watch.Stop();
                    //MessageBox.Show("Time spent: " + watch.Elapsed.Minutes + ":" + watch.Elapsed.Seconds);
                    //MessageBox.Show("Time spent: " + watch.ElapsedMilliseconds);
                    break;
                default:
                    // There should be nothing here.
                    break;
            }

            if (e.ClickCount == 2)
            {
                //map.ZoomMap(e.GetPosition(map), Math.Ceiling(map.ZoomLevel - 1.5));
            }
        }

        private void MapMouseMove(object sender, MouseEventArgs e)
        {
            var location = map.ViewToLocation(e.GetPosition(map));

            if (location != null)
            {
                var latitude = (int)Math.Round(location.Latitude * 60000d);
                var longitude = (int)Math.Round(Location.NormalizeLongitude(location.Longitude) * 60000d);
                var latHemisphere = 'N';
                var lonHemisphere = 'E';

                if (latitude < 0)
                {
                    latitude = -latitude;
                    latHemisphere = 'S';
                }

                if (longitude < 0)
                {
                    longitude = -longitude;
                    lonHemisphere = 'W';
                }

                mouseLocation.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:000} {2:00.000}\n{3} {4:000} {5:00.000}",
                    latHemisphere, latitude / 60000, (latitude % 60000) / 1000d,
                    lonHemisphere, longitude / 60000, (longitude % 60000) / 1000d);
            }
            else
            {
                mouseLocation.Text = string.Empty;
            }
        }

        private void MapMouseLeave(object sender, MouseEventArgs e)
        {
            mouseLocation.Text = string.Empty;
        }

        private void MapManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
        {
            e.TranslationBehavior.DesiredDeceleration = 0.001;
        }

        private void MapItemTouchDown(object sender, TouchEventArgs e)
        {
            var mapItem = (MapItem)sender;
            mapItem.IsSelected = !mapItem.IsSelected;
            e.Handled = true;
        }

        private void MapMouseWheel(object sender, MouseWheelEventArgs e)
        {
            CutAndSimplify(e: e);
        }

        private void MapPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) CutAndSimplify();
        }
    }
}
