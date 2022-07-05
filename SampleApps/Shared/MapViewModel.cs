using MapControl;
using System.Collections.Generic;

namespace SampleApplication
{
    public enum GeomType
    {
        Point,
        Line,
        Polygon
    }

    public class PointItem
    {
        public string Name { get; set; }

        public Location Location { get; set; }
    }

    public class PolylineItem
    {
        public LocationCollection Locations { get; set; }
    }

    public class MapViewModel
    {
        public List<PointItem> Points { get; } = new List<PointItem>();
        public List<PointItem> Pushpins { get; } = new List<PointItem>();
        public List<PolylineItem> Polylines { get; } = new List<PolylineItem>();

        public MapViewModel()
        {
            // Nothing much to do (or draw) here...
        }

        public MapViewModel(List<string> CoordsList, GeomType MyGeomType)
        {
            if (MyGeomType == GeomType.Line | MyGeomType == GeomType.Polygon)
            {
                for (int i = 0; i < CoordsList.Count; i++)
                //System.Threading.Tasks.Parallel.For(0, CoordsList.Count, i =>
                {
                    Polylines.Add(new PolylineItem { Locations = LocationCollection.Parse(CoordsList[i]) });
                    //});
                }
            }
            else if (MyGeomType == GeomType.Point)
            {
                double[] Coords = new double[2];
                string[] SplitCoords;

                for (int i = 0; i < CoordsList.Count; i++)
                {
                    SplitCoords = CoordsList[i].Split(",");
                    Coords[0] = System.Convert.ToDouble(SplitCoords[0], System.Globalization.CultureInfo.InvariantCulture);
                    Coords[1] = System.Convert.ToDouble(SplitCoords[1], System.Globalization.CultureInfo.InvariantCulture);

                    Points.Add(new PointItem {Name = "", Location = new Location(Coords[0], Coords[1])});

                    // Alternative:
                    //Pushpins.Add(new PointItem {Name = "", Location = new Location(Coords[0], Coords[1])});
                }
            }
        }
    }
}
