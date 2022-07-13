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

namespace SampleApplication
{
    public class GeomInfos
    {
        public List<string> GeoStrings;
        public double[] BBox;
    }

    public partial class MainWindow : Window
    {
        private GeomInfos PreprocessGeom(VectorData vecData)
        {
            double[] xy = new double[2];

            double MinLon = 999;
            double MinLat = 999;
            double MaxLon = -999;
            double MaxLat = -999;

            string[] NewStrParts;
            List<string> GeomStrings = new List<string>();

            for (int f = 0; f < vecData.FeatureCollection.Count; f++)
            {
                NetTopologySuite.Features.IFeature NTSFeature = vecData.FeatureCollection[f];

                NewStrParts = new string[NTSFeature.Geometry.NumPoints];
                for (int i = 0; i < NTSFeature.Geometry.NumPoints; i++)
                {
                    xy[0] = NTSFeature.Geometry.Coordinates[i][0];
                    xy[1] = NTSFeature.Geometry.Coordinates[i][1];

                    if (xy[0] < MinLat) MinLat = xy[0];
                    if (xy[0] > MaxLat) MaxLat = xy[0];
                    if (xy[1] < MinLon) MinLon = xy[1];
                    if (xy[1] > MaxLon) MaxLon = xy[1];

                    NewStrParts[i] = xy[1].ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + xy[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                GeomStrings.Add(String.Join(" ", NewStrParts));
            }
            double[] MyBBox = new double[4];
            MyBBox[0] = MinLon;
            MyBBox[1] = MaxLon;
            MyBBox[2] = MinLat;
            MyBBox[3] = MaxLat;

            GeomInfos MyGeomInfos = new GeomInfos();
            MyGeomInfos.GeoStrings = GeomStrings;
            MyGeomInfos.BBox = MyBBox;

            return (MyGeomInfos);
        }

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

            VectorData vecData = (new VectorData(openFileDialog.FileName));
            vecData.TransformToWGS84();
            GeomInfos MyGeomInfos;
            MapViewModel NewMapDrawings;

            string geometryType = vecData.FeatureCollection[0].Geometry.GeometryType;
            switch (geometryType)
            {
                case "Point" or "MultiPoint":
                    Mouse.OverrideCursor = Cursors.Wait;
                    MyGeomInfos = PreprocessGeom(vecData);
                    NewMapDrawings = new MapViewModel(MyGeomInfos.GeoStrings, GeomType.Point);
                    DataContext = NewMapDrawings;
                    Mouse.OverrideCursor = Cursors.Arrow;
                    map.ZoomToBounds(new BoundingBox(MyGeomInfos.BBox[0], MyGeomInfos.BBox[2], MyGeomInfos.BBox[1], MyGeomInfos.BBox[3]));

                    break;
                case "LineString" or "Polygon" or "MultiPolygon":
                    Mouse.OverrideCursor = Cursors.Wait;
                    System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
                    watch.Start();
                    MyGeomInfos = PreprocessGeom(vecData);
                    NewMapDrawings = new MapViewModel(MyGeomInfos.GeoStrings, GeomType.Polygon);
                    DataContext = NewMapDrawings;
                    Mouse.OverrideCursor = Cursors.Arrow;
                    watch.Stop();
                    MessageBox.Show("Time spent: " + watch.Elapsed.Minutes + ":" + watch.Elapsed.Seconds);
                    map.ZoomToBounds(new BoundingBox(MyGeomInfos.BBox[0], MyGeomInfos.BBox[2], MyGeomInfos.BBox[1], MyGeomInfos.BBox[3]));
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
    }
}
