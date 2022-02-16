/*
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

    class SmootherEngine
    {
        public const float DEFAULT_ANGLE_THRESHOLD = 35.0f;
        const float RAD_TO_DEG = 180.0f / (float)Math.PI;

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
    }
}
