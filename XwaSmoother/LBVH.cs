using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using JeremyAnsel.Xwa.Opt;
using XwaOpter;

namespace XwaSmoother
{
    using MortonCode_t = UInt32;

    public class AABB
    {
        public XwVector min, max;

        public AABB()
        {
            MakeInvalid();
        }

        public void MakeInvalid()
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
            for (int i = 0; i < 3; i++)
            {
                if (aabb.min[i] < min[i]) min[i] = aabb.min[i];
                if (aabb.max[i] > max[i]) max[i] = aabb.max[i];
            }
        }

        public XwVector GetCentroid()
        {
            XwVector V = new XwVector();

            for (int i = 0; i < 3; i++)
            {
                V[i] = (max[i] + min[i]) / 2.0f;
            }
            return V;
        }

        public override string ToString()
        {
            return "(" + min.ToString() + ")-(" + max.ToString() + ")";
        }
    }

    public class BoxRef : IComparable<BoxRef>
    {
        public AABB box;
        public int triref;
        public MortonCode_t code;
        public Vector centroid;

        public BoxRef()
        {
            box = null;
            triref = -1;
            code = 0;
        }

        public BoxRef(BoxRef boxref)
        {
            this.box = boxref.box;
            this.triref = boxref.triref;
            this.code = boxref.code;
        }

        public int CompareTo(BoxRef boxref)
        {
            return this.code.CompareTo(boxref.code);
            /*
            if (this.code > boxref.code)
                return 1;
            if (this.code < boxref.code)
                return -1;
            return 0;
            */
        }
    }

    public class BVHEncoder
    {
        // In the encoded BVH nodes we can either store triangle indices, or the triangle vertices
        // themselves. If we store indices, we're probably going to have a smaller tree, but lower
        // performance because we need to do multiple memory reads.
        // If we store the geometry in the BVH leaves, we get a bigger tree, but better performance.

        // float vertices[9]; This is all we need to store a triangle: 3 vertices, and each vertex has 3 floats
        // However, we need a way to tell if the current node is an internal node, or a geometry node. So we need
        // one more int to store flags:
        // uint32_t flags
        // Total size: 4*9 + 4 = 40 bytes
        //
        // For inner nodes, we need:
        // float aabb[6]; 2 vertices, 3 floats each
        // int32_t left, right;
        // uint32_t flags
        // Total size: 4*6 + 4*2 + 4 = 4*(6 + 2 + 1) = 4*9 = 36 bytes

        public static unsafe int EncodeFloat(byte *dst, int ofs, float X)
        {
            float* x = &X;
            byte* src = (byte*)x;
            for (int i = 0; i < 4; i++)
                dst[ofs + i] = src[i];
            return ofs + 4;
        }

        public static unsafe int EncodeInt32(byte* dst, int ofs, Int32 X)
        {
            Int32* x = &X;
            byte* src = (byte*)x;
            for (int i = 0; i < 4; i++)
                dst[ofs + i] = src[i];
            return ofs + 4;
        }

        public static unsafe int EncodeXwVector(byte* dst, int ofs, XwVector V)
        {
            float w = 1.0f;
            ofs = EncodeFloat(dst, ofs, V.x);
            ofs = EncodeFloat(dst, ofs, V.y);
            ofs = EncodeFloat(dst, ofs, V.z);
            ofs = EncodeFloat(dst, ofs, w); // Padding to get 4 32-bit dwords
            return ofs;
        }

        public static unsafe byte[] EncodeTreeNode(TreeNode T, Int32 left, Int32 right)
        {
            byte[] data = new byte[LBVH.ENCODED_TREE_NODE_SIZE];
            int ofs = 0;
            Int32 padding = 0;
            fixed (byte *dst = &data[0])
            {
                ofs = EncodeInt32(dst, ofs, T.boxRefIdx);
                ofs = EncodeInt32(dst, ofs, left);
                ofs = EncodeInt32(dst, ofs, right);
                ofs = EncodeInt32(dst, ofs, padding);
                // 16 bytes
                ofs = EncodeXwVector(dst, ofs, T.box.min);
                // 16 bytes
                ofs = EncodeXwVector(dst, ofs, T.box.max);
                // 16 bytes
            }

            if (ofs != LBVH.ENCODED_TREE_NODE_SIZE)
            {
                throw new Exception("TreeNode should be encoded in " + LBVH.ENCODED_TREE_NODE_SIZE +
                    " bytes, got " + ofs + " instead");
            }
            return data;
        }
    }

    public interface IGenericTree
    {
        bool IsLeaf();
        List<IGenericTree> GetChildren();
    }

    public class TreeNode : IGenericTree
    {
        public int boxRefIdx;
        public TreeNode left, right;
        public AABB box;
        public MortonCode_t code;

        public TreeNode()
        {
            boxRefIdx = -1;
            left = right = null;
            box = new AABB();
            code = 0;
        }

        public TreeNode(int boxRefIdx)
        {
            this.boxRefIdx = boxRefIdx;
            left = right = null;
            box = new AABB();
            code = 0;
        }

        public TreeNode(int boxRefIdx, MortonCode_t code)
        {
            this.boxRefIdx = boxRefIdx;
            this.code = code;
            left = right = null;
            box = new AABB();
        }

        public TreeNode(int boxRefIdx, TreeNode left, TreeNode right)
        {
            this.boxRefIdx = boxRefIdx;
            this.left = left;
            this.right = right;
            this.box = new AABB();
            this.code = 0;
        }

        public TreeNode(int boxRefIdx, AABB box)
        {
            this.boxRefIdx = boxRefIdx;
            this.left = null;
            this.right = null;
            this.code = 0;
            this.box = new AABB();
            this.box.Expand(box);
        }

        public bool IsLeaf()
        {
            return this.left == null && this.right == null;
        }

        public List<IGenericTree> GetChildren()
        {
            List<IGenericTree> List = new List<IGenericTree>();
            if (this.left != null)
                List.Add(this.left);
            if (this.right != null)
                List.Add(this.right);
            return List;
        }
    }

    public class BoxRefComparer : IComparer<BoxRef>
    {
        public int axis;

        public int Compare(BoxRef a, BoxRef b)
        {
            float acomp = 0, bcomp = 0;
            switch (axis)
            {
                case 0:
                    acomp = a.centroid.X;
                    bcomp = b.centroid.X;
                    break;
                case 1:
                    acomp = a.centroid.Y;
                    bcomp = b.centroid.Y;
                    break;
                case 2:
                    acomp = a.centroid.Z;
                    bcomp = b.centroid.Z;
                    break;
            }
            return acomp.CompareTo(bcomp);
        }
    }

    public class LBVH
    {
        public const float OPT_TO_METERS = 1.0f / 40.96f;
        public const int ENCODED_TREE_NODE_SIZE = 48;

        private static void Add(ref Vector A, Vector B)
        {
            A.X += B.X;
            A.Y += B.Y;
            A.Z += B.Z;
        }

        private static void Sub(ref Vector A, Vector B)
        {
            A.X -= B.X;
            A.Y -= B.Y;
            A.Z -= B.Z;
        }

        private static void Sub(ref Vector A, XwVector B)
        {
            A.X -= B.x;
            A.Y -= B.y;
            A.Z -= B.z;
        }

        private static void Mul(float K, ref Vector A)
        {
            A.X *= K;
            A.Y *= K;
            A.Z *= K;
        }

        private static void Div(ref Vector Numerator, Vector Denominator)
        {
            Numerator.X /= Denominator.X;
            Numerator.Y /= Denominator.Y;
            Numerator.Z /= Denominator.Z;
        }

        /// <summary>
        /// Normalizes A with respect to box. A will be mapped to the range 0..1 where
        /// 0 is box.min and 1 is box.max
        /// </summary>
        /// <param name="A">The input coordinates</param>
        /// <param name="box">The scene bounds</param>
        private static void Normalize(ref Vector A, AABB box)
        {
            Vector range = new Vector();
            range.X = box.max.x - box.min.x;
            range.Y = box.max.y - box.min.y;
            range.Z = box.max.z - box.min.z;

            Sub(ref A, box.min);
            Div(ref A, range);
        }

        private static string to_binary(uint X, int group)
        {
            uint mask = 0x1;
            string result = "";

            for (int i = 0; i < 32; i++)
            {
                bool bit = (X & mask) != 0;
                result = (bit ? "1" : "0") + result;
                mask <<= 1;
                if (group != -1 && (i + 1) % group == 0) result = " " + result;
            }
            return result;
        }

        private static int firstbithigh(uint X)
        {
            int pos = 31;
            uint mask = 0x1;
            while (pos >= 0)
            {
                if ((X & (mask << pos)) != 0x0)
                    return pos;
                pos--;
            }
            return pos;
        }

        private static uint SpreadBits(uint x, int offset)
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
            x = (x | (x <<  4)) & 0x00E181C3;
            x = (x | (x <<  2)) & 0x03248649;
            x = (x | (x <<  2)) & 0x09249249;

            return x << offset;
        }

        // From https://stackoverflow.com/questions/1024754/how-to-compute-a-3d-morton-number-interleave-the-bits-of-3-ints
        private static MortonCode_t GetMortonCode32(uint x, uint y, uint z)
        {
            return SpreadBits(x, 2) | SpreadBits(y, 1) | SpreadBits(z, 0);
        }

        /// <summary>
        /// Compute the 3D Morton Code for the normalized input vector
        /// </summary>
        /// <param name="V">A normalized (0..1) 3D vector</param>
        /// <returns>The 3D Morton Code for the normalized vector</returns>
        public static MortonCode_t GetMortonCode32(XwVector V)
        {
            uint x = (uint)(V.x * 1023.0f);
            uint y = (uint)(V.y * 1023.0f);
            uint z = (uint)(V.z * 1023.0f);
            return GetMortonCode32(x, y, z);
        }

        /// <summary>
        /// Compute the 3D Morton Code for the normalized input vector
        /// </summary>
        /// <param name="V">A normalized (0..1) 3D vector</param>
        /// <returns>The 3D Morton Code for the normalized vector</returns>
        public static MortonCode_t GetMortonCode32(Vector V)
        {
            uint x = (uint)(V.X * 1023.0f);
            uint y = (uint)(V.Y * 1023.0f);
            uint z = (uint)(V.Z * 1023.0f);
            return GetMortonCode32(x, y, z);
        }

#if DISABLED
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
#endif

        private static void DeleteTree(TreeNode T)
        {
            if (T == null)
                return;

            DeleteTree(T.left);
            DeleteTree(T.right);
        }

        private static void PrintTree(string level, TreeNode T)
        {
            if (T == null)
                return;
            PrintTree(level + "    ", T.right);
            if (T.boxRefIdx == -1)
                Console.WriteLine(level + T.boxRefIdx);
            else
                Console.WriteLine(level + T.boxRefIdx + "-" + to_binary(T.code, 3));
            PrintTree(level + "    ", T.left);
        }

        private static AABB Refit(TreeNode T, in List<BoxRef> boxes, out int NumTreeNodes)
        {
            if (T == null)
            {
                NumTreeNodes = 0;
                return null;
            }

            if (T.boxRefIdx != -1)
            {
                T.box.MakeInvalid();
                T.box.Expand(boxes[T.boxRefIdx].box);
                NumTreeNodes = 1;
                return T.box;
            }

            int NodesLeft, NodesRight;
            AABB boxL = Refit(T.left, boxes, out NodesLeft);
            AABB boxR = Refit(T.right, boxes, out NodesRight);
            T.box.MakeInvalid();
            if (T.left != null) T.box.Expand(boxL);
            if (T.right != null) T.box.Expand(boxR);
            NumTreeNodes = 1 + NodesLeft + NodesRight;
            return T.box;
        }

        public static void SaveOBJ(string sOutFileName, List<Vector> Vertices, List<int> Indices)
        {
            StreamWriter file = new StreamWriter(sOutFileName);
            file.WriteLine("o Test");
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vector V = Vertices[i];
                file.WriteLine("v " + V.X * OPT_TO_METERS + " " + V.Y * OPT_TO_METERS + " " + V.Z * OPT_TO_METERS);
            }
            file.WriteLine();

            for (int i = 0; i < Indices.Count; i += 3)
            {
                file.WriteLine("f " +
                    (Indices[i] + 1) + " " + (Indices[i + 1] + 1) + " " + (Indices[i + 2] + 1));
            }
            file.Close();
        }

        private static int SaveAABBToOBJ(StreamWriter file, string name, AABB box, int vertOfs)
        {
            file.WriteLine("o " + name);

            file.WriteLine(String.Format("v {0} {1} {2}",
                box.min.x * OPT_TO_METERS, box.min.y * OPT_TO_METERS, box.min.z * OPT_TO_METERS));
            file.WriteLine(String.Format("v {0} {1} {2}",
                box.max.x * OPT_TO_METERS, box.min.y * OPT_TO_METERS, box.min.z * OPT_TO_METERS));
            file.WriteLine(String.Format("v {0} {1} {2}",
                box.max.x * OPT_TO_METERS, box.max.y * OPT_TO_METERS, box.min.z * OPT_TO_METERS));
            file.WriteLine(String.Format("v {0} {1} {2}",
                box.min.x * OPT_TO_METERS, box.max.y * OPT_TO_METERS, box.min.z * OPT_TO_METERS));

            file.WriteLine(String.Format("v {0} {1} {2}",
                box.min.x * OPT_TO_METERS, box.min.y * OPT_TO_METERS, box.max.z * OPT_TO_METERS));
            file.WriteLine(String.Format("v {0} {1} {2}",
                box.max.x * OPT_TO_METERS, box.min.y * OPT_TO_METERS, box.max.z * OPT_TO_METERS));
            file.WriteLine(String.Format("v {0} {1} {2}",
                box.max.x * OPT_TO_METERS, box.max.y * OPT_TO_METERS, box.max.z * OPT_TO_METERS));
            file.WriteLine(String.Format("v {0} {1} {2}",
                box.min.x * OPT_TO_METERS, box.max.y * OPT_TO_METERS, box.max.z * OPT_TO_METERS));

            file.WriteLine(String.Format("f {0} {1}", vertOfs + 1, vertOfs + 2));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 2, vertOfs + 3));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 3, vertOfs + 4));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 4, vertOfs + 1));

            file.WriteLine(String.Format("f {0} {1}", vertOfs + 5, vertOfs + 6));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 6, vertOfs + 7));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 7, vertOfs + 8));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 8, vertOfs + 5));

            file.WriteLine(String.Format("f {0} {1}", vertOfs + 1, vertOfs + 5));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 2, vertOfs + 6));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 3, vertOfs + 7));
            file.WriteLine(String.Format("f {0} {1}", vertOfs + 4, vertOfs + 8));

            return vertOfs + 8;
        }

        public static void SaveBoxesToOBJ(string sOutFileName, List<BoxRef> Boxes)
        {
            StreamWriter file = new StreamWriter(sOutFileName);
            int vertOfs = 0, idx = 0;
            string name;
            foreach (BoxRef boxRef in Boxes)
            {
                name = "box-" + idx;
                vertOfs = SaveAABBToOBJ(file, name, boxRef.box, vertOfs);
                idx++;
            }
            file.Close();
        }

        private static int _SaveBVHToOBJ(StreamWriter file, TreeNode T, int vertOfs)
        {
            if (T == null)
                return vertOfs;
            string name = (T.IsLeaf() ? "t-" : "i-") + to_binary(T.code, -1);
            vertOfs = SaveAABBToOBJ(file, name, T.box, vertOfs);
            vertOfs = _SaveBVHToOBJ(file, T.left, vertOfs);
            vertOfs = _SaveBVHToOBJ(file, T.right, vertOfs);
            return vertOfs;
        }

        public static void SaveBVHToOBJ(string sOutFileName, TreeNode T)
        {
            StreamWriter file = new StreamWriter(sOutFileName);
            _SaveBVHToOBJ(file, T, 0);
            file.Close();
        }

        private static TreeNode BuildBinHeap(int level, in List<BoxRef> boxes, int i, int j /*, in AABB sceneBox */)
        {
            int range = (j - i) + 1;
            //Console.WriteLine("lvl: " + level + ", [" + i + ", " + j + "]");

            if (level == 1)
            {
                // This is the immediate ancestor of the leaves
                Debug.Assert(range >= 1 && range <= 2);
                TreeNode L = new TreeNode(i, boxes[i].box);
                TreeNode R = (range == 2) ? new TreeNode(j, boxes[j].box) : null;
                return new TreeNode(-1, L, R);
            }

            /*
            int axis = -1;
            float temp, max = float.MinValue;
            for (int idx = 0; idx < 3; idx++)
            {
                temp = sceneBox.max[idx] - sceneBox.min[idx];
                if (temp > max)
                {
                    axis = idx;
                    max = temp;
                }
            }
            */

            // Split the current range in two
            int rangeLeft = range / 2;
            int mid = i + rangeLeft - 1;
            //int rangeRight = range - rangeLeft;

            //Console.WriteLine("lvl: " + level + " L: [" + i + ", " + (i + rangeLeft - 1) + "]");
            //Console.WriteLine("lvl: " + level + " R: [" + (i + rangeLeft) + ", " + j + "]");
            /*
            AABB boxLeft = new AABB(), boxRight = new AABB();
            for (int idx = i; idx <= j; idx++)
            {
                if (idx <= mid)
                    boxLeft.Expand(boxes[idx].box);
                else
                    boxRight.Expand(boxes[idx].box);
            }
            */

            return new TreeNode(-1,
                BuildBinHeap(level - 1, boxes, i, mid /*, boxLeft */),
                BuildBinHeap(level - 1, boxes, mid + 1, j /*, boxRight */)
            );
        }

        private static int delta32(uint X, uint Y)
        {
            if (X == Y)
                return -1;
            return firstbithigh(X ^ Y);
        }

        /// <summary>
        /// Naive implementation of the LBVH builder, as per Karras 2012: Maximizing Parallelism in the Construcion
        /// of BVHs, Octrees and k-d Trees. This version won't win any speed prizes, but it should work, and this
        /// library isn't built for performance anyway.
        /// </summary>
        /// <param name="boxes">The sorted AABBs that will build the tree</param>
        /// <param name="i">Left index of the range to process</param>
        /// <param name="j">Right index of the range to process</param>
        /// <returns>A binary tree that represents the LBVH. All internal nodes will be missing AABBs,
        /// so this tree must be refit later.</returns>
        private static TreeNode BuildLBVH(in List<BoxRef> boxes, int i, int j)
        {
            int split_idx = -1;
            
            if (i == j)
                return new TreeNode(boxes[i].triref, boxes[i].code);

            int fbh = delta32(boxes[i].code, boxes[j].code);
            if (fbh == -1)
            {
                split_idx = (i + j) / 2;
            }
            else
            {
                // Scan the range to find the split position
                // TODO: Don't scan, do a binary search instead
                MortonCode_t mask = (MortonCode_t)(0x1 << fbh);
                for (int idx = i; idx <= j - 1; idx++)
                {
                    if ((boxes[idx].code & mask) == 0 &&
                        (boxes[idx + 1].code & mask) != 0)
                    {
                        split_idx = idx;
                        break;
                    }
                }
            }

            return new TreeNode(
                -1,
                BuildLBVH(boxes, i, split_idx),
                BuildLBVH(boxes, split_idx + 1, j)
            );
        }

        public static void ComputeBVH(string sInFileName, string sOutFileName, out string sError)
        {
            var opt = OptFile.FromFile(sInFileName);
            Console.WriteLine("Loaded " + sInFileName);
            List<Vector> Vertices = new List<Vector>();
            List<Int32> Indices = new List<int>();
            List<BoxRef> Boxes = new List<BoxRef>();
            AABB sceneBox = new AABB();
            int vertexIndexOfs = 0;
            int TriId = 0;
            sError = "";

            // Pre-pass 1:
            // * Remove all nonzero LODs
            // * Convert all faces into indexed triangles
            // * Get the scene bounds
            for (int meshIdx = 0; meshIdx < opt.Meshes.Count; meshIdx++)
            {
                var mesh = opt.Meshes[meshIdx];

                // Remove all the LODs, except for the first one (we'll handle LODs later)
                for (int i = 1; i < mesh.Lods.Count; i++)
                    mesh.Lods.RemoveAt(i);

                // Copy the vertices to their final destination
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    Vertices.Add(mesh.Vertices[i]);

                //foreach (var Lod in mesh.Lods)
                // Let's do a BVH for the first LOD for now...
                var Lod = mesh.Lods[0];
                {
                    foreach (var faceGroup in Lod.FaceGroups)
                    {
                        foreach (var face in faceGroup.Faces)
                        {
                            BoxRef boxref;
                            int NumIndices;

                            // Add the indices for the current triangle
                            Indices.Add(face.VerticesIndex.A + vertexIndexOfs);
                            Indices.Add(face.VerticesIndex.B + vertexIndexOfs);
                            Indices.Add(face.VerticesIndex.C + vertexIndexOfs);

                            // Update the scene box
                            NumIndices = Indices.Count;
                            sceneBox.Expand(Vertices[Indices[NumIndices - 3]]);
                            sceneBox.Expand(Vertices[Indices[NumIndices - 2]]);
                            sceneBox.Expand(Vertices[Indices[NumIndices - 1]]);

                            // Update the current box
                            boxref = new BoxRef();
                            boxref.triref = TriId++;
                            boxref.box = new AABB();
                            boxref.box.Expand(Vertices[Indices[NumIndices - 3]]);
                            boxref.box.Expand(Vertices[Indices[NumIndices - 2]]);
                            boxref.box.Expand(Vertices[Indices[NumIndices - 1]]);
                            Boxes.Add(boxref);

                            // Add the second triangle if this is a quad
                            if (face.VertexNormalsIndex.D != -1)
                            {
                                Indices.Add(face.VerticesIndex.A + vertexIndexOfs);
                                Indices.Add(face.VerticesIndex.C + vertexIndexOfs);
                                Indices.Add(face.VerticesIndex.D + vertexIndexOfs);

                                // Update the scene box
                                NumIndices = Indices.Count;
                                sceneBox.Expand(Vertices[Indices[NumIndices - 1]]);

                                // Update the current box
                                boxref = new BoxRef();
                                boxref.triref = TriId++;
                                boxref.box = new AABB();
                                boxref.box.Expand(Vertices[Indices[NumIndices - 3]]);
                                boxref.box.Expand(Vertices[Indices[NumIndices - 2]]);
                                boxref.box.Expand(Vertices[Indices[NumIndices - 1]]);
                                Boxes.Add(boxref);
                            }
                        }
                    }
                }
                // Console.WriteLine("Mesh " + meshIdx + " pre-processed");
                vertexIndexOfs += mesh.Vertices.Count;
            }
            Console.WriteLine("Scene bounds: " + sceneBox.ToString());

            // Pre-pass 2:
            // * Compute the Morton codes.
            foreach (BoxRef boxref in Boxes) {
                // Get the centroid
                /*
                Vector C = new Vector(0, 0, 0);
                Add(ref C, Vertices[i]);
                Add(ref C, Vertices[j]);
                Add(ref C, Vertices[k]);
                Mul(1.0f / 3.0f, ref C);
                boxref.centroid = new Vector(C.X, C.Y, C.Z);
                */

                XwVector centroid = boxref.box.GetCentroid();
                Vector C = new Vector(centroid.x, centroid.y, centroid.z);
                boxref.centroid = C;

                // Get the Morton code
                Normalize(ref C, sceneBox);
                boxref.code = GetMortonCode32(C);
            }

            // Checkpoint: save an OBJ file and check that everything looks fine
            //SaveOBJ("c:\\Temp\\LBVHInput.obj", Vertices, Indices);

            // Sort the primitives by their Morton codes
            Boxes.Sort();

            // Checkpoint: check the sort
            /*
            for (int i = 0; i < Boxes.Count - 1; i++)
            {
                if (Boxes[i].code > Boxes[i + 1].code)
                {
                    Console.WriteLine("WRONG ORDERING on elements " + i + ", " + (i + 1));
                    Console.WriteLine("code i: " + Boxes[i].code);
                    Console.WriteLine("code i+1: " + Boxes[i+1].code);
                    sError = "Wrong ordering";
                    return;
                }
            }
            */

            int numPrims = Boxes.Count;
            Console.WriteLine("numPrims: " + numPrims);
            /*
            int fbh = firstbithigh((uint)numPrims);
            int capacity = (int)(0x1 << fbh);
            Console.WriteLine("fbh: " + fbh + ", capacity: " + capacity);
            if (capacity < numPrims)
            {
                fbh++; capacity *= 2;
            }
            Console.WriteLine("final fbh: " + fbh + ", final capacity: " + capacity);
            */

            // Build the tree proper
            TreeNode T = BuildLBVH(Boxes, 0, numPrims - 1);
            // Compute the inner nodes' AABBs
            int NumTreeNodes = 0;
            Refit(T, Boxes, out NumTreeNodes);
            //PrintTree("", T);
            // DEBUG, let's dump the BVH tree structure
            //SaveBVHToOBJ("c:\\temp\\LBVH.obj", T);

            // Save the tree and the primitives
            SaveBVH(sOutFileName, NumTreeNodes, T, Vertices, Indices, out sError);
            Console.WriteLine("Saved: " + sOutFileName);

            // Tidy up
            DeleteTree(T);
            T = null;
        }

        private static void SaveBVH(string sOutFileName, int NumTreeNodes, TreeNode T, List<Vector> Vertices, List<int> Indices, out string sError)
        {
            System.IO.BinaryWriter file = new BinaryWriter(File.OpenWrite(sOutFileName));
            sError = "";

            // Save the Magic Word/Version
            {
                string Magic = "BVH2-1.0";
                byte[] data = Encoding.ASCII.GetBytes(Magic);
                file.Write(data);
            }

            // Save the vertices
            {
                // Write the number of vertices
                UInt32 NumTangents = (UInt32)Vertices.Count;
                file.Write(NumTangents);

                // Write the vertices
                float[] data = new float[3 * Vertices.Count];
                int ofs = 0;
                for (int i = 0; i < Vertices.Count; i++)
                {
                    data[ofs + 0] = Vertices[i].X;
                    data[ofs + 1] = Vertices[i].Y;
                    data[ofs + 2] = Vertices[i].Z;
                    ofs += 3;
                }

                for (int i = 0; i < ofs; i++)
                    file.Write(data[i]);
            }

            // Save the indices
            {
                // Write the number of indices
                UInt32 NumIndices = (UInt32)Indices.Count;
                file.Write(NumIndices);

                // Write the indices
                int[] data = new int[Indices.Count];
                int ofs = 0;
                for (int i = 0; i < Indices.Count; i++, ofs++)
                {
                    data[ofs] = Indices[i];
                }

                for (int i = 0; i < ofs; i++)
                    file.Write(data[i]);
            }

            // Save the LBVH
            {
                SaveLBVH(file, NumTreeNodes, T);
            }

            file.Close();
        }

        private static void SaveLBVH(BinaryWriter file, int NumTreeNodes, IGenericTree root)
        {
            if (root == null)
                return;

            // Write the number of nodes in the tree
            file.Write(NumTreeNodes);

            // A breadth-first traversal will ensure that each level of the tree is written to disk
            // before advancing to the next level. We can thus keep track of the offset in the file
            // where the next node will appear.
            Queue<IGenericTree> Q = new Queue<IGenericTree>();

            // Initialize the queue and the offsets. Note that the offsets are relative to the
            // on-disk beginning of the LBVH -- not absolute offsets from the beginning of the file.
            Q.Enqueue(root);
            // Since we're going to put this data in an array, it's easier to specify the children
            // offsets as indices into this array.
            int nextNode = 1;

            while (Q.Count != 0)
            {
                IGenericTree T = Q.Dequeue();
                List<IGenericTree> children = T.GetChildren();

                // In a breadth-first search, the left child will always be at offset nextNode
                file.Write(
                    BVHEncoder.EncodeTreeNode((TreeNode)T,
                        children.Count > 0 ? nextNode : -1,
                        children.Count > 1 ? nextNode + 1 : -1)
                );

                // Enqueue the children
                foreach (var child in children)
                {
                    Q.Enqueue(child);
                    nextNode++;
                }
            }
        }

    }
}
