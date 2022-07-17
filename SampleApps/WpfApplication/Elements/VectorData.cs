using System.Linq;

namespace SampleApplication
{
    public enum ToStringParams
    {
        GeoJSON,
        ByteString
    }

    public class VectorData
    {
        /***********************************************************************************

        Class VectorData
        Contains properties and methods for vector data handling.
        
        Internally, vector data is stored as a serialized FlatGeoBuf object,
        i.e. as a byte array. It can easily be deserialized and then is represented as
        as collection of geometries and attributes.

        The class also handles ESRI shapefiles and all other kinds of vector layers
        supported by GDAL. Loading a layer utilizes GDAL to actually import it. The data
        is then converted to FlatGeoBuf and serialized for internal storage. All
        supported vector data types can be imported and exported, loaded and saved.

        The class also contains methods to sending and receiving vector data via gRPC.
            ???

        (c) 2022 by Carsten Croonenbroeck, Markus Berger, and David Hennecke. Contact us at
        carsten.croonenbroeck@uni-rostock.de.

            Add license information here.

        Dependencies (NuGet packages):
        - MaxRev.Gdal.Core
        - MaxRev.Gdal.WindowsRuntime.Minimal
        - FlatGeobuf
        - NetTopologySuite (comes with FlatGeobuf, update if necessary)

        *///////////////////////////////////////////////////////////////////////////////////

        // Internal variables --------------------------------------------------------------

        private byte[] _vecData;
        private bool _busy;
        private OSGeo.OSR.SpatialReference _SRS;
        private string _name;
        private string _filename;
        private string _description;
        private double _ID = 0.0;

        // Getters and setters -------------------------------------------------------------

        public byte[] VecData
        {
            get { return (SerializeVec()); }
            set
            {
                if (ByteArrValid(value))
                {
                    _vecData = value;
                    HandleNameAndCRS();
                }
                else
                {
                    throw new System.Exception("Array does not represent a valid FlatGeobuf format.");
                }
            }
        }

        public bool Busy
        {
            get { return (_busy); }
        }

        public string Name
        {
            get { return (_name); }
            set { _name = value; }
        }

        public string FileName
        {
            get { return (_filename); }
            set { _filename = value; }
        }

        public string Description
        {
            get { return (_description); }
            set { _description = value; }
        }

        public OSGeo.OSR.SpatialReference SpatialReference
        {
            get { return (_SRS); }
            set { _SRS = value; }
        }

        public OSGeo.OGR.Layer Layer
        {
            get { return (GetAsLayer()); }
            set { if (value != null) _vecData = ImportLayer(value); }
        }

        public string GeoJSON
        {
            get { return (GetAsGeoJSON()); }
            set
            {
                if (value != null) _vecData = ImportFromGeoJSON(value);
            }
        }

        public NetTopologySuite.Features.FeatureCollection FeatureCollection
        {
            get
            {
                if (_vecData != null)
                {
                    NetTopologySuite.Features.FeatureCollection MyFC = FlatGeobuf.NTS.FeatureCollectionConversions.Deserialize(_vecData);
                    return (MyFC);
                }
                else
                {
                    return (null);
                }
            }

            set
            {
                if (value != null)
                {
                    _vecData = IntSerialize(value, FlatGeobuf.GeometryType.Unknown);
                    _SRS = null;
                    _name = null;
                    _description = null;
                }
            }
        }
        public double ID
        {
            get { return (_ID); }
        }

        // Constructors --------------------------------------------------------------------

        // Parameterless constructor.
        public VectorData()
        {
            MakeID();
        }

        // Constructor that accepts a string and decides what to do internally.
        // First, a Base64 coded ByteArray is assumed. If that's not it, it is assumed that the string
        // contains a file name. If that's an fgb file, open it. If not, try GDAL.
        // Examples:
        // VectorData vectorData = new VectorData(MyByteArray;
        // VectorData vectorData = new VectorData("C:/Temp/UScounties.fgb");
        // VectorData vectorData = new VectorData("C:/Temp/UScounties.shp");
        public VectorData(string MyString)
        {
            if (MyString.StartsWith("ZmdiA2ZnYg")) //Base64 for fgb header
            {
                _busy = true;
                _vecData = StringToByteArr(MyString);
                HandleNameAndCRS();
                MakeID();
                _busy = false;
            }
            else
            {
                if (System.IO.File.Exists(MyString))
                {
                    _busy = true;
                    using (System.IO.Stream SourceFile = System.IO.File.OpenRead(MyString))
                    {
                        _filename = MyString;

                        byte[] FileBuffer = new byte[8];
                        SourceFile.Read(FileBuffer, 0, FileBuffer.Length);
                        if (ByteArrValid(FileBuffer)) // Detected an fgb file.
                        {
                            _vecData = System.IO.File.ReadAllBytes(MyString);
                        }
                        else // No fgb. Try GDAL, then.
                        {
                            InitGDAL();
                            OSGeo.OGR.DataSource MyDS;
                            MyDS = OSGeo.OGR.Ogr.Open(MyString, 0);
                            if (MyDS != null)
                            {
                                _vecData = ImportLayer(MyDS.GetLayerByIndex(0));
                            }
                        }
                        HandleNameAndCRS();
                        MakeID();

                    }
                    _busy = false;
                }
                else
                {
                    throw new System.Exception("File does not exist.");
                }
            }
        }

        // Constructor that opens a rectangle out of a FlatGeobuf file.
        // Example:
        // var FilterRect = new NetTopologySuite.Geometries.Envelope(-100, -90, 40, 30);
        // VectorData vectorData = new VectorData("C:/Temp/UScounties.fgb", FilterRect);
        public VectorData(string FlatGeobufFileName, NetTopologySuite.Geometries.Envelope FlatGeobufRect)
        {
            if (System.IO.File.Exists(FlatGeobufFileName))
            {
                _busy = true;
                using (System.IO.Stream SourceFile = System.IO.File.OpenRead(FlatGeobufFileName))
                {
                    byte[] FileBuffer = new byte[8];
                    SourceFile.Read(FileBuffer, 0, FileBuffer.Length);
                    if (FileBuffer[0] != 0x66 | FileBuffer[1] != 0x67 | FileBuffer[2] != 0x62 | FileBuffer[4] != 0x66 | FileBuffer[5] != 0x67 | FileBuffer[6] != 0x62)
                    {
                        throw new System.Exception("File is not in valid FlatGeobuf format.");
                    }
                }

                _filename = FlatGeobufFileName;

                using (System.IO.Stream SourceFile = System.IO.File.OpenRead(FlatGeobufFileName))
                {
                    System.Collections.Generic.IEnumerable<NetTopologySuite.Features.IFeature> FeaturesList = FlatGeobuf.NTS.FeatureCollectionConversions.Deserialize(SourceFile, FlatGeobufRect);
                    NetTopologySuite.Features.FeatureCollection NewCollection = new NetTopologySuite.Features.FeatureCollection();
                    //foreach (NetTopologySuite.Features.IFeature ListElement in FeaturesList) { NewCollection.Add(ListElement); }
                    System.Threading.Tasks.Parallel.ForEach(FeaturesList, ListElement => { NewCollection.Add(ListElement); });

                    SourceFile.Seek(0, 0);
                    FlatGeobuf.Header MyHeader = FlatGeobuf.Helpers.ReadHeader(SourceFile);
                    HandleHeader(MyHeader);

                    _vecData = IntSerialize(NewCollection, FlatGeobuf.GeometryType.Unknown);
                    MakeID();
                }
            }
            else
            {
                throw new System.Exception("File is not in valid FlatGeobuf format.");
            }
            _busy = false;
        }

        // Constructor that is provided a GDAL layer as a data source (e.g. obtained from a shape file).
        // Example:
        // VectorData vectorData = new VectorData(LayerData);
        public VectorData(OSGeo.OGR.Layer LayerData)
        {
            if (LayerData != null)
            {
                _busy = true;
                _vecData = ImportLayer(LayerData);
                MakeID();
                _busy = false;
            }
        }

        // Constructor that is provided a byte array containing serialized FGB VectorData.
        // Example:
        // VectorData vectorData = new VectorData(VecArray);
        public VectorData(byte[] VecArray)
        {
            if (ByteArrValid(VecArray))
            {
                _busy = true;
                _vecData = VecArray;
                HandleNameAndCRS();
                MakeID();
                _busy = false;
            }
        }

        // Private methods -----------------------------------------------------------------

        // Make ID.
        private void MakeID()
        {
            System.Random rnd = new System.Random();
            _ID = rnd.NextDouble();
        }

        // Checks the FGB-validity of a provided byte array.
        private bool ByteArrValid(byte[] TestData)
        {
            if (TestData.Length >= 8)
            {
                if (TestData[0] == 0x66 && TestData[1] == 0x67 && TestData[2] == 0x62 && TestData[4] == 0x66 && TestData[5] == 0x67 && TestData[6] == 0x62)
                {
                    return (true);
                }
            }
            return (false);
        }

        // Imports a GDAL layer.
        private byte[] ImportLayer(OSGeo.OGR.Layer LayerData)
        {
            if (LayerData == null) return (null);

            OSGeo.OGR.Geometry OGRGeom;
            OSGeo.OGR.FieldType ThisType;
            NetTopologySuite.Geometries.Geometry NTSGeom;
            NetTopologySuite.Features.AttributesTable NTSAttribTable;
            NetTopologySuite.Features.Feature NTSFeature;
            byte[] WkbBuffer;
            NetTopologySuite.Features.FeatureCollection NTSFC = new NetTopologySuite.Features.FeatureCollection();

            for (int i = 0; i < LayerData.GetFeatureCount(0); i++)
            {
                OGRGeom = LayerData.GetFeature(i).GetGeometryRef();
                WkbBuffer = new byte[OGRGeom.WkbSize()];
                OGRGeom.ExportToWkb(WkbBuffer);

                NetTopologySuite.IO.WKBReader MyReader = new NetTopologySuite.IO.WKBReader();
                NTSGeom = MyReader.Read(WkbBuffer);

                NTSFeature = new NetTopologySuite.Features.Feature();
                NTSFeature.Geometry = NTSGeom;

                if (LayerData.GetFeature(i).GetFieldCount() > 0)
                {
                    NTSAttribTable = new NetTopologySuite.Features.AttributesTable();
                    for (int j = 0; j < LayerData.GetFeature(i).GetFieldCount(); j++)
                    {
                        ThisType = LayerData.GetFeature(i).GetFieldType(j);
                        if (ThisType == OSGeo.OGR.FieldType.OFTInteger)
                        {
                            NTSAttribTable.Add(LayerData.GetFeature(i).GetFieldDefnRef(j).GetName(), LayerData.GetFeature(i).GetFieldAsInteger(j));
                        }
                        else if (ThisType == OSGeo.OGR.FieldType.OFTInteger64)
                        {
                            NTSAttribTable.Add(LayerData.GetFeature(i).GetFieldDefnRef(j).GetName(), LayerData.GetFeature(i).GetFieldAsInteger64(j));
                        }
                        else if (ThisType == OSGeo.OGR.FieldType.OFTReal)
                        {
                            NTSAttribTable.Add(LayerData.GetFeature(i).GetFieldDefnRef(j).GetName(), LayerData.GetFeature(i).GetFieldAsDouble(j));
                        }
                        else
                        {
                            NTSAttribTable.Add(LayerData.GetFeature(i).GetFieldDefnRef(j).GetName(), LayerData.GetFeature(i).GetFieldAsString(j));
                        }
                    }
                    NTSFeature.Attributes = NTSAttribTable;
                }

                NTSFC.Add(NTSFeature);
            }

            _SRS = LayerData.GetSpatialRef();
            _name = LayerData.GetName();
            _description = null;

            return (IntSerialize(NTSFC, FlatGeobuf.GeometryType.Unknown));
        }

        // Returns the internal data representation as a GDAL layer.
        private OSGeo.OGR.Layer GetAsLayer()
        {
            if (_vecData == null) return (null);

            _busy = true;
            InitGDAL();

            OSGeo.OGR.Driver MyDriver = OSGeo.OGR.Ogr.GetDriverByName("ESRI Shapefile");
            OSGeo.OGR.DataSource MyDS = MyDriver.CreateDataSource("/vsimem/Temporary", new string[] { });
            OSGeo.OGR.Layer MyLayer = MyDS.CreateLayer(_name + MyDS.GetLayerCount().ToString(), _SRS, OSGeo.OGR.wkbGeometryType.wkbUnknown, new string[] { });
            OSGeo.OGR.FeatureDefn MyFeatureDefn = MyLayer.GetLayerDefn();
            OSGeo.OGR.Feature MyFeature;
            OSGeo.OGR.FieldDefn MyFieldDefn = null;
            NetTopologySuite.Features.FeatureCollection MyFC = FlatGeobuf.NTS.FeatureCollectionConversions.Deserialize(_vecData);
            NetTopologySuite.IO.WKBWriter MyWriter;
            byte[] WKB;
            string[] AttribNames = MyFC[0].Attributes.GetNames();
            string[] FieldTypes = new string[AttribNames.Length];

            if (MyFC[0].Attributes.Count > 0)
            {
                // Prepare fields.
                for (int i = 0; i < MyFC[0].Attributes.Count; i++)
                {
                    FieldTypes[i] = MyFC[0].Attributes.GetType(AttribNames[i]).ToString();
                    if (FieldTypes[i] == "System.Int32")
                    {
                        MyFieldDefn = new OSGeo.OGR.FieldDefn(AttribNames[i], OSGeo.OGR.FieldType.OFTInteger);
                    }
                    else if (FieldTypes[i] == "System.Int64")
                    {
                        MyFieldDefn = new OSGeo.OGR.FieldDefn(AttribNames[i], OSGeo.OGR.FieldType.OFTInteger64);
                    }
                    else if (FieldTypes[i] == "System.Double")
                    {
                        MyFieldDefn = new OSGeo.OGR.FieldDefn(AttribNames[i], OSGeo.OGR.FieldType.OFTReal);
                    }
                    else
                    {
                        MyFieldDefn = new OSGeo.OGR.FieldDefn(AttribNames[i], OSGeo.OGR.FieldType.OFTString);
                    }

                    MyLayer.CreateField(MyFieldDefn, 0);
                    MyFieldDefn.Dispose();
                }

                for (int i = 0; i < MyFC.Count; i++)
                {
                    // Handle geometry.
                    MyWriter = new NetTopologySuite.IO.WKBWriter();
                    WKB = MyWriter.Write(MyFC[i].Geometry);
                    OSGeo.OGR.Geometry OGRGeom = OSGeo.OGR.Geometry.CreateFromWkb(WKB);
                    MyFeature = new OSGeo.OGR.Feature(MyFeatureDefn);
                    MyFeature.SetGeometry(OGRGeom);

                    // Handle fields.
                    if (MyFC[i].Attributes.Count > 0)
                    {
                        object[] AttribValuesObj = MyFC[i].Attributes.GetValues();
                        for (int j = 0; j < MyFC[i].Attributes.Count; j++)
                        {
                            if (FieldTypes[j] == "System.Int32")
                            {
                                MyFeature.SetField(j, System.Convert.ToInt32(AttribValuesObj[j]));
                            }
                            else if (FieldTypes[j] == "System.Int64")
                            {
                                MyFeature.SetField(j, System.Convert.ToInt64(AttribValuesObj[j]));
                            }
                            else if (FieldTypes[j] == "System.Double")
                            {
                                MyFeature.SetField(j, System.Convert.ToDouble(AttribValuesObj[j]));
                            }
                            else
                            {
                                MyFeature.SetField(j, AttribValuesObj[j].ToString());
                            }
                        }
                    }

                    MyLayer.CreateFeature(MyFeature);
                    MyFeature.Dispose();
                }
                MyFeatureDefn.Dispose();
                MyDS.SyncToDisk();
                MyDriver.Dispose();
            }

            _busy = false;
            return (MyLayer);
        }

        // Returns the internal data representation as a GeoJSON string.
        private string GetAsGeoJSON()
        {
            if (_vecData == null) return (null);

            _busy = true;
            NetTopologySuite.IO.GeoJsonWriter MyWriter = new NetTopologySuite.IO.GeoJsonWriter();
            string GeoJSON = MyWriter.Write(FlatGeobuf.NTS.FeatureCollectionConversions.Deserialize(_vecData));
            _busy = false;

            return (GeoJSON);
        }

        // Imports a GeoJSON string.
        private byte[] ImportFromGeoJSON(string JSONData)
        {
            if (JSONData == null) return (null);

            _busy = true;
            NetTopologySuite.IO.GeoJsonReader MyReader = new NetTopologySuite.IO.GeoJsonReader();
            NetTopologySuite.Features.FeatureCollection MyFC = MyReader.Read<NetTopologySuite.Features.FeatureCollection>(JSONData);
            byte[] MyData = null;
            if (MyFC != null) MyData = IntSerialize(MyFC, FlatGeobuf.GeometryType.Unknown);
            InitGDAL();
            _SRS = new OSGeo.OSR.SpatialReference(null);
            _SRS.ImportFromEPSG(4326);
            _name = null;
            _description = null;
            _busy = false;

            return (MyData);
        }

        private void HandleHeader(FlatGeobuf.Header MyHeader)
        {
            InitGDAL();
            _SRS = new OSGeo.OSR.SpatialReference(null);

            _name = MyHeader.Name;
            _description = MyHeader.Description;
            FlatGeobuf.Crs? MyCRS = MyHeader.Crs;
            string WKTString = MyCRS?.Wkt;
            int Code = (int)MyCRS?.Code;
            string Organization = MyCRS?.Org;
            _SRS.ImportFromWkt(ref WKTString);

            string Proj4;
            _SRS.ExportToProj4(out Proj4);
            if (Proj4 == "")
            {
                if (Organization == "EPSG") _SRS.ImportFromEPSG(Code);
            }
        }

        private void HandleNameAndCRS()
        {
            using (System.IO.MemoryStream MemStream = new System.IO.MemoryStream(_vecData))
            {
                FlatGeobuf.Header MyHeader = FlatGeobuf.Helpers.ReadHeader(MemStream);
                if (MyHeader.Crs != null) HandleHeader(MyHeader);
            }
        }

        // Subsequent stuff here stolen and modified from https://github.com/flatgeobuf/flatgeobuf/blob/master/src/net/FlatGeobuf/NTS/FeatureCollectionConversions.cs#L116.
        private static FlatGeobuf.ColumnType ToColumnType(System.Type type)
        {
            if (System.Type.GetTypeCode(type) == System.TypeCode.Byte) return (FlatGeobuf.ColumnType.UByte);
            if (System.Type.GetTypeCode(type) == System.TypeCode.SByte) return (FlatGeobuf.ColumnType.Byte);
            if (System.Type.GetTypeCode(type) == System.TypeCode.Boolean) return (FlatGeobuf.ColumnType.Bool);
            if (System.Type.GetTypeCode(type) == System.TypeCode.Int32) return (FlatGeobuf.ColumnType.Int);
            if (System.Type.GetTypeCode(type) == System.TypeCode.Int64) return (FlatGeobuf.ColumnType.Long);
            if (System.Type.GetTypeCode(type) == System.TypeCode.Double) return (FlatGeobuf.ColumnType.Double);
            if (System.Type.GetTypeCode(type) == System.TypeCode.String) return (FlatGeobuf.ColumnType.String);

            throw new System.ApplicationException("Unknown type");
        }

        private byte[] SerializeVec()
        {
            if (_vecData == null) return (null);

            NetTopologySuite.Features.FeatureCollection MyFC = FlatGeobuf.NTS.FeatureCollectionConversions.Deserialize(_vecData);
            byte[] retVal = IntSerialize(MyFC, FlatGeobuf.GeometryType.Unknown);

            return (retVal);
        }

        private byte[] IntSerialize(NetTopologySuite.Features.FeatureCollection fc, FlatGeobuf.GeometryType geometryType, byte dimensions = 2, System.Collections.Generic.IList<FlatGeobuf.NTS.ColumnMeta> columns = null)
        {
            NetTopologySuite.Features.IFeature featureFirst = fc.First();
            if (columns == null && featureFirst.Attributes != null)
            {
                columns = featureFirst.Attributes.GetNames()
                          .Select(n => new FlatGeobuf.NTS.ColumnMeta() { Name = n, Type = ToColumnType(featureFirst.Attributes.GetType(n)) })
                          .ToList();
            }
            System.IO.MemoryStream memoryStream = new System.IO.MemoryStream();
            IntSerialize(memoryStream, fc, geometryType, dimensions, columns);
            return (memoryStream.ToArray());
        }

        private void IntSerialize(System.IO.Stream output, System.Collections.Generic.IEnumerable<NetTopologySuite.Features.IFeature> features, FlatGeobuf.GeometryType geometryType, byte dimensions = 2, System.Collections.Generic.IList<FlatGeobuf.NTS.ColumnMeta> columns = null)
        {
            Nito.AsyncEx.AsyncContext.Run(async () => await IntSerializeAsync(output, features, geometryType, dimensions, columns));
        }

        private async System.Threading.Tasks.Task IntSerializeAsync(System.IO.Stream output, System.Collections.Generic.IEnumerable<NetTopologySuite.Features.IFeature> features, FlatGeobuf.GeometryType geometryType, byte dimensions = 2, System.Collections.Generic.IList<FlatGeobuf.NTS.ColumnMeta> columns = null)
        {
            await output.WriteAsync(FlatGeobuf.Constants.MagicBytes, 0, FlatGeobuf.Constants.MagicBytes.Length);
            FlatBuffers.ByteBuffer headerBuffer = IntBuildHeader(0, geometryType, dimensions, columns, null);
            byte[] bytes = headerBuffer.ToSizedArray();
            await output.WriteAsync(bytes, 0, bytes.Length);
            headerBuffer.Position = headerBuffer.Position + 4;
            FlatGeobuf.HeaderT header = FlatGeobuf.Header.GetRootAsHeader(headerBuffer).UnPack();
            foreach (NetTopologySuite.Features.IFeature feature in features)
            {
                FlatBuffers.ByteBuffer buffer = FlatGeobuf.NTS.FeatureConversions.ToByteBuffer(feature, header);
                bytes = buffer.ToSizedArray();
                await output.WriteAsync(bytes, 0, bytes.Length);
            }
        }

        private FlatBuffers.ByteBuffer IntBuildHeader(ulong count, FlatGeobuf.GeometryType geometryType, byte dimensions, System.Collections.Generic.IList<FlatGeobuf.NTS.ColumnMeta> columns, FlatGeobuf.Index.PackedRTree index)
        {
            string WKTString;
            _SRS.ExportToWkt(out WKTString, null);
            string WKTStart = null;
            string CRSName = "";
            if (WKTString.Contains("["))
            {
                WKTStart = WKTString.Substring(0, WKTString.IndexOf("["));
                CRSName = _SRS.GetAttrValue(WKTStart, 0);
            }
            string Organization = _SRS.GetAuthorityName(null);
            int Code = System.Convert.ToInt32(_SRS.GetAuthorityCode(null));

            FlatBuffers.FlatBufferBuilder builder = new FlatBuffers.FlatBufferBuilder(1024);

            FlatBuffers.Offset<FlatGeobuf.Crs> MyCRS = FlatGeobuf.Crs.CreateCrs(
                                                       builder,
                                                       builder.CreateString(Organization),
                                                       Code,
                                                       builder.CreateString(CRSName),
                                                       builder.CreateString(""),
                                                       builder.CreateString(WKTString),
                                                       builder.CreateString(Code.ToString()));

            FlatBuffers.StringOffset NameO = builder.CreateString("");
            if (_name != null) NameO = builder.CreateString(_name);
            FlatBuffers.StringOffset DescO = builder.CreateString("");
            if (_description != null) DescO = builder.CreateString(_description);

            FlatBuffers.VectorOffset? columnsOffset = null;
            if (columns != null)
            {
                FlatBuffers.Offset<FlatGeobuf.Column>[] columnsArray = columns
                                   .Select(c => FlatGeobuf.Column.CreateColumn(builder, builder.CreateString(c.Name), c.Type))
                                   .ToArray();
                columnsOffset = FlatGeobuf.Header.CreateColumnsVector(builder, columnsArray);
            }

            FlatGeobuf.Header.StartHeader(builder);

            FlatGeobuf.Header.AddCrs(builder, MyCRS);
            FlatGeobuf.Header.AddGeometryType(builder, geometryType);
            if (_name != null) FlatGeobuf.Header.AddName(builder, NameO);
            if (_description != null) FlatGeobuf.Header.AddDescription(builder, DescO);
            if (dimensions == 3) FlatGeobuf.Header.AddHasZ(builder, true);
            if (dimensions == 4) FlatGeobuf.Header.AddHasM(builder, true);
            if (columnsOffset.HasValue) FlatGeobuf.Header.AddColumns(builder, columnsOffset.Value);
            if (index != null)
            {
                FlatGeobuf.Header.AddIndexNodeSize(builder, 16);
            }
            else
            {
                FlatGeobuf.Header.AddIndexNodeSize(builder, 0);
            }

            FlatGeobuf.Header.AddFeaturesCount(builder, count);
            FlatBuffers.Offset<FlatGeobuf.Header> offset = FlatGeobuf.Header.EndHeader(builder);

            builder.FinishSizePrefixed(offset.Value);

            return (builder.DataBuffer);
        }

        private void PrepareFields(OSGeo.OGR.Layer InLayer, ref OSGeo.OGR.Layer OutLayer)
        {
            OSGeo.OGR.FeatureDefn InFeatureDefn = InLayer.GetLayerDefn();

            OSGeo.OGR.FieldDefn FieldDefn = null;
            for (int j = 0; j < InFeatureDefn.GetFieldCount(); j++)
            {
                if (InLayer.GetFeature(0).GetFieldType(j) == OSGeo.OGR.FieldType.OFTReal)
                {
                    FieldDefn = new OSGeo.OGR.FieldDefn(InFeatureDefn.GetFieldDefn(j).GetName(), OSGeo.OGR.FieldType.OFTReal);
                }
                else if (InLayer.GetFeature(0).GetFieldType(j) == OSGeo.OGR.FieldType.OFTInteger64)
                {
                    FieldDefn = new OSGeo.OGR.FieldDefn(InFeatureDefn.GetFieldDefn(j).GetName(), OSGeo.OGR.FieldType.OFTInteger64);
                }
                else if (InLayer.GetFeature(0).GetFieldType(j) == OSGeo.OGR.FieldType.OFTInteger)
                {
                    FieldDefn = new OSGeo.OGR.FieldDefn(InFeatureDefn.GetFieldDefn(j).GetName(), OSGeo.OGR.FieldType.OFTInteger);
                }
                else
                {
                    FieldDefn = new OSGeo.OGR.FieldDefn(InFeatureDefn.GetFieldDefn(j).GetName(), OSGeo.OGR.FieldType.OFTString);
                }

                OutLayer.CreateField(FieldDefn, 0);
                FieldDefn.Dispose();
            }
        }

        private void CopyFields(OSGeo.OGR.Feature InFeature, int numFields, ref OSGeo.OGR.Feature OutFeature)
        {
            for (int j = 0; j < numFields; j++)
            {
                if (InFeature.GetFieldType(j) == OSGeo.OGR.FieldType.OFTReal)
                {
                    OutFeature.SetField(j, InFeature.GetFieldAsDouble(j));
                }
                else if (InFeature.GetFieldType(j) == OSGeo.OGR.FieldType.OFTInteger64)
                {
                    OutFeature.SetField(j, InFeature.GetFieldAsInteger64(j));
                }
                else if (InFeature.GetFieldType(j) == OSGeo.OGR.FieldType.OFTInteger)
                {
                    OutFeature.SetField(j, InFeature.GetFieldAsInteger(j));
                }
                else
                {
                    OutFeature.SetField(j, InFeature.GetFieldAsString(j));
                }
            }
        }

        // Static methods ------------------------------------------------------------------

        // Static helper that initializes GDAL.
        public static void InitGDAL()
        {
            MaxRev.Gdal.Core.GdalBase.ConfigureAll();
            OSGeo.OGR.Ogr.RegisterAll();

            OSGeo.GDAL.Gdal.SetConfigOption("GDAL_FILENAME_IS_UTF8", "NO");
            OSGeo.GDAL.Gdal.SetConfigOption("SHAPE_ENCODING", "");
            OSGeo.GDAL.Gdal.SetConfigOption("PROJ_DEBUG", "5");
        }

        public static string ByteArrToString(byte[] ByteArr)
        {
            return (System.Convert.ToBase64String(ByteArr));
        }

        public static byte[] StringToByteArr(string ByteStr)
        {
            return (System.Convert.FromBase64String(ByteStr));
        }

        // Methods -------------------------------------------------------------------------

        public string ToString(ToStringParams? Params)
        {
            string MyString = null;

            if (_vecData != null)
            {
                if (Params == ToStringParams.GeoJSON)
                {
                    MyString = GetAsGeoJSON();
                }
                else if (Params == ToStringParams.ByteString)
                {
                    MyString = ByteArrToString(_vecData);
                }
                else
                {
                    NetTopologySuite.Features.FeatureCollection MyFC = FlatGeobuf.NTS.FeatureCollectionConversions.Deserialize(_vecData);
                    MyString = "Number of features: " + MyFC.Count + ". To obtain the data as a GeoJSON string, use \".ToString(ToStringParams.GeoJSON)\" or method \".GetAsGeoJSON()\" directly.";
                }
            }

            return (MyString);
        }

        // Saves the data natively as a FlatGeobuf file.
        public void SaveAsFGB(string FileName)
        {
            _busy = true;
            byte[] FGB = SerializeVec();
            System.IO.File.WriteAllBytes(FileName, FGB);
            _busy = false;
        }

        public int TransformToWGS84() // Returns 0 on success and 1 if an error occurred.
        {
            _busy = true;
            InitGDAL();

            OSGeo.OGR.Driver ShapeDriver = OSGeo.OGR.Ogr.GetDriverByName("ESRI Shapefile");
            OSGeo.OGR.Layer InLayer = GetAsLayer();
            string LayerName = InLayer.GetName();

            OSGeo.OSR.SpatialReference FromSRS = InLayer.GetSpatialRef();
            FromSRS.SetAxisMappingStrategy(OSGeo.OSR.AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);

            OSGeo.OSR.SpatialReference ToCRS = new OSGeo.OSR.SpatialReference(null);
            OSGeo.OSR.SpatialReference ToWGS84 = new OSGeo.OSR.SpatialReference(null);

            ToCRS = FromSRS.CloneGeogCS();
            ToCRS.SetAxisMappingStrategy(OSGeo.OSR.AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            OSGeo.OSR.CoordinateTransformation TransformToCRS = new OSGeo.OSR.CoordinateTransformation(FromSRS, ToCRS);
            ToWGS84.SetWellKnownGeogCS("EPSG:4326");
            ToWGS84.SetAxisMappingStrategy(OSGeo.OSR.AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            OSGeo.OSR.CoordinateTransformation TransformToWGS84 = new OSGeo.OSR.CoordinateTransformation(ToCRS, ToWGS84);

            OSGeo.OGR.DataSource OutDS = ShapeDriver.CreateDataSource("/vsimem/Temporary", new string[] { });
            OSGeo.OGR.Layer OutLayer = OutDS.CreateLayer(LayerName + "_WGS84", ToWGS84, InLayer.GetGeomType(), new string[] { });

            OSGeo.OGR.FeatureDefn InFeatureDefn = InLayer.GetLayerDefn();

            PrepareFields(InLayer, ref OutLayer);

            OSGeo.OGR.Geometry OGRGeom;
            for (long i = 0; i < InLayer.GetFeatureCount(0); i++)
            {
                OGRGeom = InLayer.GetFeature(i).GetGeometryRef();

                if (OGRGeom.Transform(TransformToCRS) == OSGeo.OGR.Ogr.OGRERR_NONE)
                {
                    if (OGRGeom.Transform(TransformToWGS84) == OSGeo.OGR.Ogr.OGRERR_NONE)
                    {
                        OSGeo.OGR.Feature OutFeature = new OSGeo.OGR.Feature(InFeatureDefn);

                        OutFeature.SetGeometry(OGRGeom);
                        CopyFields(InLayer.GetFeature(i), InFeatureDefn.GetFieldCount(), ref OutFeature);

                        OutLayer.CreateFeature(OutFeature);
                        OutFeature.Dispose();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Error during tranformation.");
                        InLayer.Dispose();
                        OutLayer.Dispose();
                        OutDS.Dispose();
                        ShapeDriver.Dispose();

                        _busy = false;
                        return (1);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Error during tranformation.");
                    InLayer.Dispose();
                    OutLayer.Dispose();
                    OutDS.Dispose();
                    ShapeDriver.Dispose();

                    _busy = false;
                    return (1);
                }
            }

            InFeatureDefn.Dispose();
            OutDS.SyncToDisk();
            Layer = OutLayer; // "Layer" is the setter of the VectorData class, meaning that here the instance is actually being updated.
            InLayer.Dispose();
            OutLayer.Dispose();
            OutDS.Dispose();
            ShapeDriver.Dispose();

            _busy = false;
            return (0);
        }

        public int ProjectTo(int EPSG)
        {
            string Proj4;

            OSGeo.OSR.SpatialReference TempSRS = new OSGeo.OSR.SpatialReference(null);
            TempSRS.ImportFromEPSG(EPSG);
            TempSRS.ExportToProj4(out Proj4);

            return (ProjectTo(Proj4));
        }

        public int ProjectTo(string Proj4)
        {
            _busy = true;
            this.TransformToWGS84();

            OSGeo.OGR.Driver ShapeDriver = OSGeo.OGR.Ogr.GetDriverByName("ESRI Shapefile");
            OSGeo.OGR.Layer InLayer = GetAsLayer();
            string LayerName = InLayer.GetName();

            OSGeo.OSR.SpatialReference FromSRS = InLayer.GetSpatialRef();
            FromSRS.SetAxisMappingStrategy(OSGeo.OSR.AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);

            if (Proj4.ToLower().Contains("projection[") | Proj4.ToLower().Contains("geogcs[") | Proj4.ToLower().Contains("projcs["))
            {
                OSGeo.OSR.SpatialReference TempSRS = new OSGeo.OSR.SpatialReference(null);
                if (TempSRS.ImportFromWkt(ref Proj4) != OSGeo.OGR.Ogr.OGRERR_NONE)
                {
                    _busy = false;
                    return (1);
                }
                TempSRS.ExportToProj4(out Proj4);
            }
            OSGeo.OSR.SpatialReference ToSRS = new OSGeo.OSR.SpatialReference(null);
            if (ToSRS.ImportFromProj4(Proj4) != OSGeo.OGR.Ogr.OGRERR_NONE)
            {
                _busy = false;
                return (1);
            }
            ToSRS.SetAxisMappingStrategy(OSGeo.OSR.AxisMappingStrategy.OAMS_AUTHORITY_COMPLIANT);

            OSGeo.OSR.CoordinateTransformation Project = new OSGeo.OSR.CoordinateTransformation(FromSRS, ToSRS);

            OSGeo.OGR.DataSource OutDS = ShapeDriver.CreateDataSource("/vsimem/Temporary", new string[] { });
            OSGeo.OGR.Layer OutLayer = OutDS.CreateLayer(LayerName + "_Project", ToSRS, InLayer.GetGeomType(), new string[] { });

            OSGeo.OGR.FeatureDefn InFeatureDefn = InLayer.GetLayerDefn();

            PrepareFields(InLayer, ref OutLayer);

            OSGeo.OGR.Geometry OGRGeom;
            for (long i = 0; i < InLayer.GetFeatureCount(0); i++)
            {
                OGRGeom = InLayer.GetFeature(i).GetGeometryRef();

                if (OGRGeom.Transform(Project) == OSGeo.OGR.Ogr.OGRERR_NONE)
                {
                    OSGeo.OGR.Feature OutFeature = new OSGeo.OGR.Feature(InFeatureDefn);

                    OutFeature.SetGeometry(OGRGeom);
                    CopyFields(InLayer.GetFeature(i), InFeatureDefn.GetFieldCount(), ref OutFeature);

                    OutLayer.CreateFeature(OutFeature);
                    OutFeature.Dispose();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Error during tranformation.");
                    InLayer.Dispose();
                    OutLayer.Dispose();
                    OutDS.Dispose();
                    ShapeDriver.Dispose();

                    _busy = false;
                    return (1);
                }
            }

            InFeatureDefn.Dispose();
            OutDS.SyncToDisk();
            Layer = OutLayer; // "Layer" is the setter of the VectorData class, meaning that here the instance is actually being updated.
            InLayer.Dispose();
            OutLayer.Dispose();
            OutDS.Dispose();
            ShapeDriver.Dispose();

            _busy = false;
            return (0);
        }
    }

    // Derived classes -------------------------------------------------------------------------

    public class VectorPointData : VectorData
    {
        public VectorPointData() : base() { }
        public VectorPointData(string MyString) : base(MyString) { }
        public VectorPointData(string FlatGeobufFileName, NetTopologySuite.Geometries.Envelope FlatGeobufRect) : base(FlatGeobufFileName, FlatGeobufRect) { }
        public VectorPointData(OSGeo.OGR.Layer LayerData) : base(LayerData) { }
        public VectorPointData(byte[] VecArray) : base(VecArray) { }
    }

    public class VectorLineData : VectorData
    {
        public VectorLineData() : base() { }
        public VectorLineData(string MyString) : base(MyString) { }
        public VectorLineData(string FlatGeobufFileName, NetTopologySuite.Geometries.Envelope FlatGeobufRect) : base(FlatGeobufFileName, FlatGeobufRect) { }
        public VectorLineData(OSGeo.OGR.Layer LayerData) : base(LayerData) { }
        public VectorLineData(byte[] VecArray) : base(VecArray) { }
    }

    public class VectorPolygonData : VectorData
    {
        public VectorPolygonData() : base() { }
        public VectorPolygonData(string MyString) : base(MyString) { }
        public VectorPolygonData(string FlatGeobufFileName, NetTopologySuite.Geometries.Envelope FlatGeobufRect) : base(FlatGeobufFileName, FlatGeobufRect) { }
        public VectorPolygonData(OSGeo.OGR.Layer LayerData) : base(LayerData) { }
        public VectorPolygonData(byte[] VecArray) : base(VecArray) { }
    }

    public class VectorMultiPolygonData : VectorData
    {
        public VectorMultiPolygonData() : base() { }
        public VectorMultiPolygonData(string MyString) : base(MyString) { }
        public VectorMultiPolygonData(string FlatGeobufFileName, NetTopologySuite.Geometries.Envelope FlatGeobufRect) : base(FlatGeobufFileName, FlatGeobufRect) { }
        public VectorMultiPolygonData(OSGeo.OGR.Layer LayerData) : base(LayerData) { }
        public VectorMultiPolygonData(byte[] VecArray) : base(VecArray) { }
    }
}