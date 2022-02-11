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
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace XwaOpter
{
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

#if DISABLED
    public class VertexMap
    {
        private Dictionary<string, XwVector> _map;
        
        public VertexMap()
        {
            _map = new Dictionary<string, XwVector>();
        }

        ~VertexMap()
        {
            _map.Clear();
        }

        public bool find(XwVector Vertex, out XwVector Normal)
        {
            if (!_map.TryGetValue(Hash.VertexHash(Vertex), out Normal))
                return false;
            return true;
        }

        public void insert_or_add(XwVector Vertex, XwVector Normal)
        {
            string sHash = Hash.VertexHash(Vertex);
            XwVector Temp;
            find(Vertex, out Temp);

            Temp.x += Normal.x;
            Temp.y += Normal.y;
            Temp.z += Normal.z;
            _map[sHash] = Temp;
        }
    }
#endif

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
            _map = new Dictionary<string, List<string>> ();
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

    class Program
    {
        const float RAD_TO_DEG = 180.0f / (float)Math.PI;
        const float DEFAULT_ANGLE_THRESHOLD = 35.0f;

        static string GetOpenFile()
        {
            var dialog = new OpenFileDialog();
            dialog.DefaultExt = ".opt";
            dialog.CheckFileExists = true;
            dialog.Filter = "OPT files (*.opt)|*.opt";

            Config config = Config.ReadConfigFile();

            if (!string.IsNullOrEmpty(config.OpenOptDirectory))
            {
                dialog.InitialDirectory = config.OpenOptDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                config.OpenOptDirectory = Path.GetDirectoryName(dialog.FileName);
                config.SaveConfigFile();

                return dialog.FileName;
            }

            return null;
        }

        static string GetSaveAsFile(string fileName)
        {
            fileName = Path.GetFullPath(fileName);
            var dialog = new SaveFileDialog();
            dialog.AddExtension = true;
            dialog.DefaultExt = ".opt";
            dialog.Filter = "OPT files (*.opt)|*.opt";
            dialog.InitialDirectory = Path.GetDirectoryName(fileName);
            dialog.FileName = Path.GetFileName(fileName);

            Config config = Config.ReadConfigFile();

            if (!string.IsNullOrEmpty(config.SaveOptDirectory))
            {
                dialog.InitialDirectory = config.SaveOptDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                config.SaveOptDirectory = Path.GetDirectoryName(dialog.FileName);
                config.SaveConfigFile();

                return dialog.FileName;
            }

            return null;
        }

        static XwVector ComputeNormal(Mesh mesh, Face face)
        {
            XwVector N = new XwVector(0,0,0), temp;
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
                    XwVector AN;
                    //Console.WriteLine("\tAdjacent to face: " + faceHash);
                    // N is already initialized to FN, so we can skip the current face.
                    if (faceHash != curFaceHash && faceNormalMap.find(faceHash, out AN))
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

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Smooth OPT normals 1.0");

            string sInFileName = GetOpenFile();
            string sOutFileName = GetSaveAsFile(sInFileName);
            var opt = OptFile.FromFile(sInFileName);
            Console.WriteLine("Loaded: " + sInFileName);

            List<int> targetMeshIndices = new List<int>();
            string targetMeshIndexString = Microsoft.VisualBasic.Interaction.InputBox(
                "Mesh index:\n-1 means whole OPT\nUse commas to specify multiple meshes (i.e. 0,1,2)",
                "Mesh index",
                "-1");
            if (!string.IsNullOrEmpty(targetMeshIndexString))
            {
                string[] indicesString = targetMeshIndexString.Split(',');
                foreach (var indexString in indicesString)
                {
                    int targetIdx = int.Parse(indexString, CultureInfo.InvariantCulture);
                    if (targetIdx == -1)
                    {
                        targetMeshIndices.Clear();
                        break;
                    } else 
                        targetMeshIndices.Add(targetIdx);
                }
            }

            float threshold = DEFAULT_ANGLE_THRESHOLD;
            string thresholdString = Microsoft.VisualBasic.Interaction.InputBox(
                "Normals threshold in degrees:", 
                "Normals threshold",
                threshold.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(thresholdString))
            {
                threshold = float.Parse(thresholdString, CultureInfo.InvariantCulture);
            }

            int meshesProcessed = 0;
            FaceNormalMap faceNormalMap = new FaceNormalMap();
            VertexFaceListMap vertexFaceListMap = new VertexFaceListMap();
            for (int meshIdx = 0; meshIdx < opt.Meshes.Count; meshIdx++)  
            {
                var mesh = opt.Meshes[meshIdx];
                if (targetMeshIndices.Count() > 0 && !targetMeshIndices.Contains(meshIdx))
                    continue;

                mesh.VertexNormals.Clear();
                //var Lod = mesh.Lods[0];
                foreach (var Lod in mesh.Lods)
                {
                    Console.WriteLine("-------------------------------------");
                    Console.WriteLine("Processing Mesh: " + meshIdx + ", MeshType: " + mesh.Descriptor.MeshType);
                    Console.WriteLine("Vertices: " + mesh.Vertices.Count + ", Normals: " + mesh.VertexNormals.Count);
                    Console.WriteLine("Angle: " + threshold);
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
            //Console.WriteLine("Saving to: " + sOutFileName);
            opt.Save(sOutFileName);
            Console.WriteLine("OPT saved to: " + sOutFileName);
            Console.WriteLine(meshesProcessed + " meshes processed");

#if DEBUG
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
#endif
        }
    }
}
