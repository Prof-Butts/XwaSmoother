﻿/*
 * Algorithm pseudocode:
 * 
 * Create a map from faces to normals: one normal per face
 * Create a vertex adjacency map: vertex X is part of faces A, B, C
 * 
 * Preprocess:
 * For each face F:
 *      Compute the normal N for F
 *      normalMap[F] = N (add the normal to the map)
 *      Populate the adjacency map for each vertex V in F:
 *      For each vertex V in F:
 *          adjMap[V].append(F) (populate the adjacency map)
 * 
 * Actual computation
 * For each face F:
 *      FN = normalMap[F] (fetch the normal for F)
 *      for each vertex V in face:
 *          Initialize vertex normal N to FN
 *          for each face AF != F in adjMap[V]:
 *              AN = normalMap[AF]
 *              if angle(FN, AN) < threshold:
 *                  N += AN
 *          normalize N (this is the per-vertex-per-face smooth normal)
 *          Add N to the normals list for V for face F
 *      Update the face normal indices
 */
using JeremyAnsel.Xwa.Opt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using XwaOpter;

namespace XwaSmoother
{
    /*
     * Helper class that computes unique hash keys for OPT vertices and faces
     */
    public class Hash
    {
        public static string VertexHash(XwVector V)
        {
            unsafe
            {
                float x = (float)V.x;
                float y = (float)V.y;
                float z = (float)V.z;

                uint* ux = (uint*)&x;
                uint* uy = (uint*)&y;
                uint* uz = (uint*)&z;

                return (*ux).ToString("x") + (*uy).ToString("x") + (*uz).ToString("x");
            }
        }

        public static string FaceHash(Face face)
        {
            string S = "";
            S += face.VerticesIndex.A + ";";
            S += face.VerticesIndex.B + ";";
            S += face.VerticesIndex.C + ";";
            S += face.VerticesIndex.D + ";";
            return S;
        }
    }

    /*
     * Maps one face to one normal.
     */
    public class FaceNormalMap
    {
        private Dictionary<string, XwVector> _map;

        public FaceNormalMap()
        {
            _map = new Dictionary<string, XwVector>();
        }

        ~FaceNormalMap()
        {
            _map.Clear();
        }

        public bool find(Face face, out XwVector Normal)
        {
            if (!_map.TryGetValue(Hash.FaceHash(face), out Normal))
                return false;
            return true;
        }

        public bool find(string faceHash, out XwVector Normal)
        {
            if (!_map.TryGetValue(faceHash, out Normal))
                return false;
            return true;
        }

        public void insert(Face face, XwVector Normal)
        {
            _map[Hash.FaceHash(face)] = Normal;
        }
    }

    /*
     * Maps one vertex to a list of faces.
     */
    public class VertexFaceListMap
    {
        private Dictionary<string, List<string>> _map;

        public VertexFaceListMap()
        {
            _map = new Dictionary<string, List<string>>();
        }

        ~VertexFaceListMap()
        {
            _map.Clear();
        }

        public bool find(XwVector V, out List<string> faceList)
        {
            if (!_map.TryGetValue(Hash.VertexHash(V), out faceList))
                return false;
            return true;
        }

        public void insert(XwVector V, Face face)
        {
            // TODO: Can a vertex be adjacent to the same face multiple times?
            List<string> faceList;
            string faceHash = Hash.FaceHash(face);
            if (find(V, out faceList))
            {
                faceList.Add(faceHash);
            }
            else
            {
                faceList = new List<string>();
                faceList.Add(faceHash);
            }
            _map[Hash.VertexHash(V)] = faceList;
        }
    }

    public class AABB
    {
        public XwVector min, max;

        public AABB()
        {
            min.SetMaxFloat();
            max.SetMinFloat();
        }

        public void Expand(XwVector V)
        {
            for (int i = 0; i < 3; i++)
            {
                if (V[i] < min[i]) min[i] = V[i];
                if (V[i] > max[i]) max[i] = V[i];
            }
        }

        public void Expand(Vector V)
        {
            if (V.X < min.x) min.x = V.X;
            if (V.Y < min.y) min.y = V.Y;
            if (V.Z < min.z) min.z = V.Z;

            if (V.X > max.x) max.x = V.X;
            if (V.Y > max.y) max.y = V.Y;
            if (V.Z > max.z) max.z = V.Z;
        }

        public void Expand(AABB aabb)
        {
            for (int i = 0; i < 3; i++) {
                if (aabb.min[i] < min[i]) min[i] = aabb.min[i];
                if (aabb.max[i] < max[i]) max[i] = aabb.max[i];
            }
        }

        public XwVector GetCentroid()
        {
            XwVector V;
            V.x = (max.x + min.x) / 2.0f;
            V.y = (max.y + min.y) / 2.0f;
            V.z = (max.z + min.z) / 2.0f;
            return V;
        }
    }

    public class LBVH
    {
        // From https://stackoverflow.com/questions/1024754/how-to-compute-a-3d-morton-number-interleave-the-bits-of-3-ints
        public static uint GetMortonCode(uint x, uint y, uint z)
        {
            return SpreadBits(x, 0) | SpreadBits(y, 1) | SpreadBits(z, 2);
        }

        public static uint SpreadBits(uint x, int offset)
        {
            if ((x < 0) || (x > 1023))
            {
                throw new ArgumentOutOfRangeException();
            }

            if ((offset < 0) || (offset > 2))
            {
                throw new ArgumentOutOfRangeException();
            }

            x = (x | (x << 10)) & 0x000F801F;
            x = (x | (x << 4)) & 0x00E181C3;
            x = (x | (x << 2)) & 0x03248649;
            x = (x | (x << 2)) & 0x09249249;

            return x << offset;
        }

        /// <summary>
        /// Compute the 3D Morton Code for the normalized input vector
        /// </summary>
        /// <param name="V">A normalized (0..1) 3D vector</param>
        /// <returns>The 3D Morton Code for the normalized vector</returns>
        public static uint GetMortonCode(XwVector V)
        {
            uint x = (uint)(V.x * 1023.0f);
            uint y = (uint)(V.y * 1023.0f);
            uint z = (uint)(V.z * 1023.0f);
            return GetMortonCode(x, y, z);
        }

        public static List<AABB> GetAABB(Mesh mesh, Face face)
        {
            List<AABB> result = new List<AABB>();
            AABB aabb;

            aabb = new AABB();
            aabb.Expand(mesh.Vertices[face.VerticesIndex.A]);
            aabb.Expand(mesh.Vertices[face.VerticesIndex.B]);
            aabb.Expand(mesh.Vertices[face.VerticesIndex.C]);
            result.Add(aabb);

            if (face.VerticesIndex.D != -1)
            {
                aabb = new AABB();
                aabb.Expand(mesh.Vertices[face.VerticesIndex.A]);
                aabb.Expand(mesh.Vertices[face.VerticesIndex.C]);
                aabb.Expand(mesh.Vertices[face.VerticesIndex.D]);
                result.Add(aabb);
            }

            return result;
        }
        
    }

    public class SmootherEngine
    {
        public const float DEFAULT_ANGLE_THRESHOLD = 35.0f;
        public const UInt32 COCKPIT_ID_BITS = 0x200;
        public const UInt32 EXTERIOR_ID_BITS = 0x400;
        public const UInt32 HANGAR_ID_BITS = 0x800;
        const float RAD_TO_DEG = 180.0f / (float)Math.PI;

        public static string sXwaRootDirectory = "";

        /// <summary>
        /// The in-memory copy of FlightModels\SPACECRAFT0.LST. Needs sXwaRootDirectory to
        /// be set first. This Dictionary can be used to tag OPT meshes with a unique ID,
        /// which later can be used to side-load additional per-mesh information, like
        /// precomputed tangent maps, AABBs or BVHs...
        /// </summary>
        private static Dictionary<string, UInt32> Spacecraft0List = null;

        private static void CacheSpacecraftList(out string sError)
        {
            if (sXwaRootDirectory.Length == 0)
            {
                sError = "The XWA root directory is not set";
                return;
            }

            if (Spacecraft0List != null)
            {
                // No error, the spacecraft list has already been cached
                sError = "";
                return;
            }

            string sFileName = Path.Combine(sXwaRootDirectory, "FlightModels", "SPACECRAFT0.LST");
            if (!File.Exists(sFileName))
            {
                sError = "File " + sFileName + " does not exist";
                return;
            }

            Spacecraft0List = new Dictionary<string, uint>();
            UInt32 ID = 1; // Avoid storing an ID of 0
            StreamReader reader = new StreamReader(new FileStream(sFileName, FileMode.Open));
            while (!reader.EndOfStream) {
                string sLine = reader.ReadLine();
                // Each line in spacecraft0.lst has the form:
                // FlightModels\OPTname.opt
                // We want to extract OPTname from this line
                sLine = sLine.Trim().ToUpper();
                string sRootFileName = Path.GetFileNameWithoutExtension(sLine);

                // Some entries can be duplicated, and some don't even have a corresponding
                // OPT file at all!
                // Also, some OPTs have Cockpit, Exterior or Hangar versions. Plus there's
                // an OPT called "Hangar.opt" that doesn't even appear in the list. Fun.
                // I'm going to need a map from string to ID
                Spacecraft0List[sRootFileName] = ID;
                ID++;
            }

            reader.Close();
            sError = "";
            Console.WriteLine((ID - 1) + " OPT names cached");
        }

        /// <summary>
        /// Computes a unique ID for the given OPT name.
        /// </summary>
        /// <param name="sOPTName">The OPT name to get the ID of. Must contain the ".OPT" extension</param>
        /// <param name="sError">The error message (if any)</param>
        /// <returns>0 if the unique ID couldn't be generated, nonzero otherwise.</returns>
        public static UInt32 GetUniqueOPTId(string sOPTName, out string sError)
        {
            UInt32 ID = 0;
            sError = "";
            sOPTName = sOPTName.ToUpper();

            // Cache the space craft list or bail out on error.
            if (Spacecraft0List == null)
            {
                CacheSpacecraftList(out sError);
                if (sError.Length != 0)
                {
                    // Something went wrong, bail out
                    return 0;
                }

                if (Spacecraft0List == null)
                {
                    sError = "Could not cache SPACECRAFT0.LST";
                    return 0;
                }
            }

            // Special case: "HANGAR.OPT" doesn't appear in the list, but it does exist
            if (sOPTName == "HANGAR.OPT")
                // Our lowest valid ID is 1, so we can return HANGAR_ID_BITS because it
                // won't collide with any other ID (i.e. we can't do HANGAR_ID_BITS | 0x0)
                // from the regular path
                return HANGAR_ID_BITS;

            string sBaseName = sOPTName;
            UInt32 CtrlBits = 0x0;

            // Check if this is a hangar/remove the .OPT extension
            if (sBaseName.Contains("HANGAR.OPT"))
            {
                CtrlBits |= HANGAR_ID_BITS;
                sBaseName = sBaseName.Replace("HANGAR.OPT", "");
            }
            else
                sBaseName = sBaseName.Replace(".OPT", "");

            // Check for Cockpit OPT
            if (sBaseName.Contains("COCKPIT"))
            {
                sBaseName = sBaseName.Replace("COCKPIT", "");
                CtrlBits |= COCKPIT_ID_BITS;
            }

            // Check for Exterior OPT
            if (sBaseName.Contains("EXTERIOR"))
            {
                sBaseName = sBaseName.Replace("EXTERIOR", "");
                CtrlBits |= EXTERIOR_ID_BITS;
            }

            if (!Spacecraft0List.ContainsKey(sBaseName))
            {
                sError = sBaseName + " is not part of SPACECRAFT0.LST";
                return 0;
            }

            ID = Spacecraft0List[sBaseName];
            return ID | CtrlBits;
        }

        static XwVector ComputeNormal(Mesh mesh, Face face)
        {
            XwVector N = new XwVector(0, 0, 0), temp;
            XwVector p1 = new XwVector(mesh.Vertices[face.VerticesIndex.A]);
            XwVector p2 = new XwVector(mesh.Vertices[face.VerticesIndex.B]);
            XwVector p3 = new XwVector(mesh.Vertices[face.VerticesIndex.C]);

            XwVector e1 = XwVector.Substract(p2, p3);
            XwVector e2 = XwVector.Substract(p1, p2);
            temp = XwVector.CrossProduct(e1, e2);
            N = XwVector.Add(N, temp);

            if (face.VerticesIndex.D != -1)
            {
                XwVector p4 = new XwVector(mesh.Vertices[face.VerticesIndex.D]);

                XwVector e3 = XwVector.Substract(p4, p1);
                XwVector e4 = XwVector.Substract(p3, p4);
                temp = XwVector.CrossProduct(e3, e4);
                N = XwVector.Add(N, temp);
            }

            N = XwVector.Normalize(N);
            return N;
        }

        // https://www.gamedeveloper.com/programming/messing-with-tangent-space has an
        // interesting method for computing the tangent. Summary:
        // T = e21.xyz / e21.u
        // Maybe I'll try that later.

        // From: http://www.opengl-tutorial.org/intermediate-tutorials/tutorial-13-normal-mapping/
        static XwVector ComputeTangent(Mesh mesh, Face face)
        {
            bool quad = face.VerticesIndex.D != -1;
            XwVector v0 = new XwVector(mesh.Vertices[face.VerticesIndex.A]);
            XwVector v1 = new XwVector(mesh.Vertices[face.VerticesIndex.B]);
            XwVector v2 = new XwVector(mesh.Vertices[face.VerticesIndex.C]);
            //XwVector v3 = quad ? new XwVector(mesh.Vertices[face.VerticesIndex.D]) : new XwVector();

            TextureCoordinates uv0 = mesh.TextureCoordinates[face.TextureCoordinatesIndex.A];
            TextureCoordinates uv1 = mesh.TextureCoordinates[face.TextureCoordinatesIndex.B];
            TextureCoordinates uv2 = mesh.TextureCoordinates[face.TextureCoordinatesIndex.C];
            //TextureCoordinates uv3 = quad ? mesh.TextureCoordinates[face.TextureCoordinatesIndex.D] : new TextureCoordinates();

            XwVector deltaPos1 = XwVector.Substract(v1, v0);
            XwVector deltaPos2 = XwVector.Substract(v2, v0);

            XwVector deltaUV1 = new XwVector(uv1.U - uv0.U, uv1.V - uv0.V, 0);
            XwVector deltaUV2 = new XwVector(uv2.U - uv0.U, uv2.V - uv0.V, 0);

            // We're not going to worry about a division by zero here. If that happens, we have either
            // a collapsed triangle or a triangle with collapsed UVs. If it's a collapsed triangle, we
            // won't see it as it will render as a line. If it's collapsed UVs, then the modeller must
            // fix the UVs anyway.
            float r = 1.0f / (deltaUV1.x * deltaUV2.y - deltaUV1.y * deltaUV2.x);
            XwVector T = XwVector.Multiply(
                XwVector.Substract(XwVector.Multiply(deltaPos1, deltaUV2.y), 
                                   XwVector.Multiply(deltaPos2, deltaUV1.y)
                                  ),
                r);
            //XwVector bitangent = (deltaPos2 * deltaUV1.x - deltaPos1 * deltaUV2.x) * r;
            return XwVector.Normalize(T);
        }

        static void SaveTangentMap(XwVector[] Tangents, string sOutFileName)
        {
            System.IO.BinaryWriter file = new BinaryWriter(File.OpenWrite(sOutFileName));
            // Write the number of tangents
            UInt32 NumTangents = (UInt32)Tangents.Length;
            file.Write(NumTangents);

            // Write the tangents
            float[] data = new float[3 * Tangents.Length];
            int ofs = 0;
            for (int i = 0; i < Tangents.Length; i++)
            {
                data[ofs + 0] = Tangents[i].x;
                data[ofs + 1] = Tangents[i].y;
                data[ofs + 2] = Tangents[i].z;
                ofs += 3;
            }

            for (int i = 0; i < ofs; i++)
                file.Write(data[i]);
        }

        public static uint ComputeTangentMap(string sInFileName, string sOutFileName, out string sError)
        {
            if (sXwaRootDirectory.Length == 0)
            {
                sError = "XWA root directory is not set";
                return 0;
            }

            // Create the output directory if necessary
            string sTangentDir = Path.Combine(sXwaRootDirectory, "Effects", "TangentMaps");
            if (!Directory.Exists(sTangentDir))
                Directory.CreateDirectory(sTangentDir);

            string sRootFileName = Path.GetFileName(sInFileName);
            UInt32 OPTId = GetUniqueOPTId(sRootFileName, out sError) << 16;
            if (sError.Length != 0)
                return 0;

            var opt = OptFile.FromFile(sInFileName);
            Console.WriteLine("Loaded " + sInFileName + "\nRoot: " + sRootFileName + "\nOPTId: " + OPTId);

            UInt32 OPTMeshId = 1;
            for (int meshIdx = 0; meshIdx < opt.Meshes.Count; meshIdx++)
            {
                var mesh = opt.Meshes[meshIdx];
                
                //foreach (var Lod in mesh.Lods)
                // We're only going to create the tangent map for the first LoD. I'm not even
                // sure we can tell which LoD is being rendered in XWA, plus smaller LoDs will
                // hardly benefit from a tangent map anyway.
                // Note: In XWA we need to make sure that the number of normals maps the number
                // of tangents.
                var Lod = mesh.Lods[0]; 
                {
                    XwVector[] VertexTangents = new XwVector[mesh.VertexNormals.Count];
                    // Compute the tangents for each face
                    foreach (var faceGroup in Lod.FaceGroups)
                    {
                        foreach (var face in faceGroup.Faces)
                        {
                            int idx;
                            XwVector T = ComputeTangent(mesh, face);

                            // Let's assume that this mesh already has smoothed normals. In that case,
                            // we can "transfer" the smooth groups to the tangent map by re-orthogonalizing
                            // T with N (doing the cross product with N twice).

                            idx = face.VertexNormalsIndex.A;
                            VertexTangents[idx] = Orthogonalize(new XwVector(mesh.VertexNormals[idx]), T);
                            //VertexTangents[idx] = T;

                            idx = face.VertexNormalsIndex.B;
                            VertexTangents[idx] = Orthogonalize(new XwVector(mesh.VertexNormals[idx]), T);
                            //VertexTangents[idx] = T;

                            idx = face.VertexNormalsIndex.C;
                            VertexTangents[idx] = Orthogonalize(new XwVector(mesh.VertexNormals[idx]), T);
                            //VertexTangents[idx] = T;

                            if (face.VerticesIndex.D != -1)
                            {
                                idx = face.VertexNormalsIndex.D;
                                VertexTangents[idx] = Orthogonalize(new XwVector(mesh.VertexNormals[idx]), T);
                                //VertexTangents[idx] = T;
                            }
                        }
                    }

                    UInt32 UniqueID = OPTId | OPTMeshId;
                    if (sError.Length == 0)
                    {
                        string sTangentFileName = Path.Combine(sTangentDir, UniqueID.ToString("X") + ".tan");
                        Console.WriteLine("OPTId: " + OPTId.ToString("X") + ", " +
                            "OPTMeshId: " + OPTMeshId.ToString("X") + ", " +
                            "UniqueID: " + UniqueID.ToString("X"));
                        SaveTangentMap(VertexTangents, sTangentFileName);
                    } else
                    {
                        return 0;
                    }

                    mesh.Descriptor.TargetId = (int)UniqueID;
                    OPTMeshId++;
                }
            }
            opt.Save(sOutFileName);
            sError = "";
            return OPTMeshId - 1;
        }

        static XwVector Orthogonalize(XwVector N, XwVector T)
        {
            XwVector B = XwVector.CrossProduct(T, N);
            return XwVector.CrossProduct(N, B);
        }

        static XwVector SmoothNormal(Face face, float threshold, XwVector V, VertexFaceListMap vertexFaceListMap, FaceNormalMap faceNormalMap)
        {
            XwVector FN, N;
            if (!faceNormalMap.find(face, out FN))
            {
                Console.WriteLine("ERROR: Face " + face.ToString() + " doesn't have a normal!");
                N.x = N.y = N.z = 0.0f;
                return N;
            }
            FN = XwVector.Normalize(FN);
            // Initialize the smooth normal to the face normal
            N = new XwVector(FN.x, FN.y, FN.z);
            string curFaceHash = Hash.FaceHash(face);
            //Console.WriteLine("A: " + face.VerticesIndex.A + ", V: " + V.ToString());

            // Fetch the adjacency list for the current vertex
            List<string> faceList;
            if (vertexFaceListMap.find(V, out faceList))
            {
                foreach (var faceHash in faceList)
                {
                    //Console.WriteLine("\tAdjacent to face: " + faceHash);
                    // N is already initialized to FN, so we can skip the current face.
                    if (faceHash != curFaceHash && faceNormalMap.find(faceHash, out XwVector AN))
                    {
                        // Accumulate the adjacent normal if the angle is right
                        AN = XwVector.Normalize(AN);
                        float angle = (float)Math.Acos(XwVector.DotProduct(FN, AN)) * RAD_TO_DEG;
                        if (angle <= threshold)
                            N = XwVector.Add(N, AN);
                    }
                }
            }
            N = XwVector.Normalize(N);
            return N;
        }

        /*
         * Smooths the normals of sInFileName and writes them to sOutFileName.
         * Threshold is a mesh-index to angle-threshold map. Meshes without an entry in this map are skipped.
         */
        public static int Smooth(string sInFileName, string sOutFileName, Dictionary<int, float> Thresholds)
        {
            float threshold = DEFAULT_ANGLE_THRESHOLD;
            bool bGlobalMeshOverride = Thresholds.ContainsKey(-1);
            if (bGlobalMeshOverride)
                threshold = Thresholds[-1];

            var opt = OptFile.FromFile(sInFileName);
            Console.WriteLine("Loaded " + sInFileName);

            int meshesProcessed = 0;
            FaceNormalMap faceNormalMap = new FaceNormalMap();
            VertexFaceListMap vertexFaceListMap = new VertexFaceListMap();
            for (int meshIdx = 0; meshIdx < opt.Meshes.Count; meshIdx++)
            {
                // No global mesh override, check each mesh threshold
                if (!bGlobalMeshOverride) {
                    if (!Thresholds.ContainsKey(meshIdx))
                        continue;
                    threshold = Thresholds[meshIdx];
                }

                var mesh = opt.Meshes[meshIdx];
                mesh.VertexNormals.Clear();
                foreach (var Lod in mesh.Lods)
                {
                    Console.WriteLine("-------------------------------------");
                    Console.WriteLine("Processing Mesh: " + meshIdx + ", MeshType: " + mesh.Descriptor.MeshType);
                    Console.WriteLine("Vertices: " + mesh.Vertices.Count + ", Normals: " + mesh.VertexNormals.Count);
                    Console.WriteLine("Angle: " + threshold);
                    // Compute new normals for each face and create the adjacency map
                    foreach (var faceGroup in Lod.FaceGroups)
                    {
                        foreach (var face in faceGroup.Faces)
                        {
                            // Compute the normal for the current face and store it
                            XwVector N = ComputeNormal(mesh, face);
                            N = XwVector.Normalize(N);
                            faceNormalMap.insert(face, N);

                            // Update the vertex -> face adjacency map
                            XwVector VA = new XwVector(mesh.Vertices[face.VerticesIndex.A]);
                            vertexFaceListMap.insert(VA, face);

                            XwVector VB = new XwVector(mesh.Vertices[face.VerticesIndex.B]);
                            vertexFaceListMap.insert(VB, face);

                            XwVector VC = new XwVector(mesh.Vertices[face.VerticesIndex.C]);
                            vertexFaceListMap.insert(VC, face);

                            if (face.VerticesIndex.D != -1)
                            {
                                XwVector VD = new XwVector(mesh.Vertices[face.VerticesIndex.D]);
                                vertexFaceListMap.insert(VD, face);
                            }
                        }
                    }

                    // Do the actual smoothing
                    foreach (var faceGroup in Lod.FaceGroups)
                    {
                        foreach (var face in faceGroup.Faces)
                        {
                            XwVector FN;
                            // Fetch the normal of this face
                            if (!faceNormalMap.find(face, out FN))
                            {
                                Console.WriteLine("ERROR: Face " + face.ToString() + " doesn't have a normal!");
                                continue;
                            }

                            XwVector V, N;

                            V = new XwVector(mesh.Vertices[face.VerticesIndex.A]);
                            N = SmoothNormal(face, threshold, V, vertexFaceListMap, faceNormalMap);
                            N = XwVector.Normalize(N);
                            mesh.VertexNormals.Add(new Vector((float)N.x, (float)N.y, (float)N.z));
                            //Console.WriteLine("V: " + V.ToString() + ", N: " + N.ToString());

                            V = new XwVector(mesh.Vertices[face.VerticesIndex.B]);
                            N = SmoothNormal(face, threshold, V, vertexFaceListMap, faceNormalMap);
                            N = XwVector.Normalize(N);
                            mesh.VertexNormals.Add(new Vector((float)N.x, (float)N.y, (float)N.z));
                            //Console.WriteLine("V: " + V.ToString() + ", N: " + N.ToString());

                            V = new XwVector(mesh.Vertices[face.VerticesIndex.C]);
                            N = SmoothNormal(face, threshold, V, vertexFaceListMap, faceNormalMap);
                            N = XwVector.Normalize(N);
                            mesh.VertexNormals.Add(new Vector((float)N.x, (float)N.y, (float)N.z));
                            //Console.WriteLine("V: " + V.ToString() + ", N: " + N.ToString());

                            if (face.VerticesIndex.D != -1)
                            {
                                V = new XwVector(mesh.Vertices[face.VerticesIndex.D]);
                                N = SmoothNormal(face, threshold, V, vertexFaceListMap, faceNormalMap);
                                N = XwVector.Normalize(N);
                                mesh.VertexNormals.Add(new Vector((float)N.x, (float)N.y, (float)N.z));
                                //Console.WriteLine("V: " + V.ToString() + ", N: " + N.ToString());
                            }

                            int count = mesh.VertexNormals.Count;
                            if (face.VerticesIndex.D == -1)
                            {
                                if (count - 3 < 0)
                                    Console.WriteLine("ERROR: Negative indices (-3)");
                                face.VertexNormalsIndex = new Index(count - 3, count - 2, count - 1);
                            }
                            else
                            {
                                if (count - 4 < 0)
                                    Console.WriteLine("ERROR: Negative indices (-4)");
                                face.VertexNormalsIndex = new Index(count - 4, count - 3, count - 2, count - 1);
                            }
                        }
                    }

                    meshesProcessed++;
                    Console.WriteLine("-------------------------------------");
                    //break;
                }
            }
            opt.Save(sOutFileName);
            Console.WriteLine("OPT saved to: " + sOutFileName);
            Console.WriteLine(meshesProcessed + " meshes processed");
            return meshesProcessed;
        }

        public static Dictionary<int, float> ParseIndices(List<string> sThresholds, out string sError)
        {
            Dictionary<int, float> Thresholds = new Dictionary<int, float>();
            sError = null;

            // Parse the thresholds string. For each string, the expected format is:
            // Idx1, Idx2-Idx3, ..:Threshold
            // An Idx of -1 means "apply threshold to all the meshes"
            foreach (string sSingleThreshold in sThresholds)
            {
                // Remove all whitespaces
                string sThreshold = sSingleThreshold.Replace(" ", "");

                // Split into <Indices>:<Threshold>
                string[] sValues = sThreshold.Split(':');
                if (sValues.Length < 2)
                {
                    sError = "Missing colon, expected <Mesh-Index>:<Angle>";
                    return null;
                }

                // Parse the threshold
                float threshold = DEFAULT_ANGLE_THRESHOLD;
                if (!float.TryParse(sValues[1], out threshold))
                {
                    sError = sValues[1] + " is not a valid number";
                    return null;
                }

                // If the indices contain a -1, we're done
                if (sValues[0].Contains("-1"))
                {
                    Thresholds.Clear();
                    Thresholds[-1] = threshold;
                    return Thresholds;
                }

                // Parse the indices
                string[] sTokens = sValues[0].Split(',');
                foreach (string sToken in sTokens)
                {
                    // Parse the range indicator
                    if (sToken.Contains(".."))
                    {
                        string[] splitter = new string[1];
                        splitter[0] = "..";
                        string[] sIndices = sToken.Split(splitter, StringSplitOptions.None);
                        int idx0, idx1;
                        if (!int.TryParse(sIndices[0], out idx0))
                            continue;
                        if (!int.TryParse(sIndices[1], out idx1))
                            continue;
                        for (int i = idx0; i <= idx1; i++)
                            Thresholds[i] = threshold;
                    }
                    else
                    {
                        // Parse an individual index
                        if (!int.TryParse(sToken, out int idx))
                            continue;
                        Thresholds[idx] = threshold;
                    }
                }
            }
            return Thresholds;
        }

        public static int Smooth(string sInFileName, string sOutFileName, List<string> sThresholds)
        {
            Dictionary<int, float> Thresholds = ParseIndices(sThresholds, out string sError);
            if (Thresholds == null)
            {
                Console.WriteLine("Error: " + sError);
                return 0;
            }

            return Smooth(sInFileName, sOutFileName, Thresholds);
        }

        public static void Test(string sInFileName, string sOutFileName)
        {
            var opt = OptFile.FromFile(sInFileName);
            Console.WriteLine("Loaded: " + sInFileName);
            for (int meshIdx = 0; meshIdx < opt.Meshes.Count; meshIdx++)
            {
                var mesh = opt.Meshes[meshIdx];
                Console.WriteLine("meshIdx: " + meshIdx);
                Console.WriteLine("Descriptor MeshType: " + mesh.Descriptor.MeshType); // MainHull, Default, etc...
                mesh.Descriptor.TargetId = meshIdx;
                Console.WriteLine("Descriptor.Target: " + mesh.Descriptor.Target + ", " + mesh.Descriptor.TargetId);
            }
            opt.Save(sOutFileName);
        }
    }
}
