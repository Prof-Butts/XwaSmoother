﻿using System;
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
        public int TriID;
        public MortonCode_t code;
        public Vector centroid;

        public BoxRef()
        {
            box = null;
            TriID = -1;
            code = 0;
        }

        public BoxRef(BoxRef boxref)
        {
            this.box = boxref.box;
            this.TriID = boxref.TriID;
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

        public static unsafe int EncodeXwVector4(byte* dst, int ofs, XwVector V)
        {
            float w = 1.0f;
            ofs = EncodeFloat(dst, ofs, V.x);
            ofs = EncodeFloat(dst, ofs, V.y);
            ofs = EncodeFloat(dst, ofs, V.z);
            ofs = EncodeFloat(dst, ofs, w); // Padding to get 4 32-bit dwords
            return ofs;
        }

        public static unsafe int EncodeXwVector3(byte* dst, int ofs, XwVector V)
        {
            ofs = EncodeFloat(dst, ofs, V.x);
            ofs = EncodeFloat(dst, ofs, V.y);
            ofs = EncodeFloat(dst, ofs, V.z);
            return ofs;
        }

        public static unsafe byte[] EncodeAABB(in AABB aabb)
        {
            byte[] data = new byte[sizeof(float) * 3 * 2];
            int ofs = 0;
            fixed (byte* dst = &data[0])
            {
                ofs = EncodeXwVector3(dst, ofs, aabb.min);
                ofs = EncodeXwVector3(dst, ofs, aabb.max);
            }
            return data;
        }

        public static unsafe byte[] EncodeTreeNode(TreeNode T, Int32 parent, Int32 left, Int32 right)
        {
            byte[] data = new byte[LBVH.ENCODED_TREE_NODE_SIZE];
            int ofs = 0;
            fixed (byte *dst = &data[0])
            {
                ofs = EncodeInt32(dst, ofs, T.boxRefIdx);
                ofs = EncodeInt32(dst, ofs, left);
                ofs = EncodeInt32(dst, ofs, right);
                ofs = EncodeInt32(dst, ofs, parent);
                // 16 bytes
                ofs = EncodeXwVector4(dst, ofs, T.box.min);
                // 16 bytes
                ofs = EncodeXwVector4(dst, ofs, T.box.max);
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
        IGenericTree GetParent();
    }

    public class TreeNode : IGenericTree
    {
        public int boxRefIdx;
        public TreeNode left, right, parent;
        public AABB box;
        public MortonCode_t code;

        public TreeNode()
        {
            boxRefIdx = -1;
            left = right = parent = null;
            box = new AABB();
            code = 0;
        }

        public TreeNode(int boxRefIdx)
        {
            this.boxRefIdx = boxRefIdx;
            left = right = parent = null;
            box = new AABB();
            code = 0;
        }

        public TreeNode(int boxRefIdx, MortonCode_t code)
        {
            this.boxRefIdx = boxRefIdx;
            this.code = code;
            left = right = parent = null;
            box = new AABB();
        }

        public TreeNode(int boxRefIdx, TreeNode left, TreeNode right)
        {
            this.boxRefIdx = boxRefIdx;
            this.left = left;
            this.right = right;
            this.parent = null;
            this.box = new AABB();
            this.code = 0;
        }

        public TreeNode(int boxRefIdx, AABB box, TreeNode left, TreeNode right)
        {
            this.boxRefIdx = boxRefIdx;
            this.left = left;
            this.right = right;
            this.parent = null;
            this.box = box;
            this.code = 0;
        }

        public TreeNode(int boxRefIdx, TreeNode left, TreeNode right, TreeNode parent)
        {
            this.boxRefIdx = boxRefIdx;
            this.left = left;
            this.right = right;
            this.parent = parent;
            this.box = new AABB();
            this.code = 0;
        }

        public TreeNode(int boxRefIdx, AABB box)
        {
            this.boxRefIdx = boxRefIdx;
            this.left = null;
            this.right = null;
            this.parent = null;
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

        public IGenericTree GetParent()
        {
            return this.parent;
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
            Console.WriteLine(level + T.boxRefIdx);
            /*
            if (T.boxRefIdx == -1)
                Console.WriteLine(level + T.boxRefIdx);
            else
                Console.WriteLine(level + T.boxRefIdx + "-" + to_binary(T.code, 3));
            */
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

            int NumNodesL, NumNodesR;
            AABB boxL = Refit(T.left, boxes, out NumNodesL);
            AABB boxR = Refit(T.right, boxes, out NumNodesR);
            T.box.MakeInvalid();
            if (T.left != null)
            {
                T.box.Expand(boxL);
                T.left.parent = T;
            }
            if (T.right != null)
            {
                T.box.Expand(boxR);
                T.right.parent = T;
            }
            NumTreeNodes = 1 + NumNodesL + NumNodesR;
            return T.box;
        }

        private static int CountNodes(TreeNode T)
        {
            if (T == null)
                return 0;

            if (T.boxRefIdx != -1)
                return 1;

            return 1 + CountNodes(T.left) + CountNodes(T.right);
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

        private static int SaveTriangleToOBJ(StreamWriter file, string name, int TriID, int vertOfs,
            in List<Vector> Vertices, in List<int> Indices)
        {
            if (name != null)
                file.WriteLine("o " + name);

            // Write the vertices
            for (int i = 0; i < 3; i++)
            {
                int ofs = TriID * 3 + i;
                Vector V = Vertices[Indices[ofs]];
                file.WriteLine(String.Format("v {0} {1} {2}",
                    V.X * OPT_TO_METERS, V.Y * OPT_TO_METERS, V.Z * OPT_TO_METERS));
            }
            file.WriteLine();

            // Write the face
            file.WriteLine(String.Format("f {0} {1} {2}",
                // OBJ files use indices that are 1-based, so we add +1,2,3 here, instead of
                // 0,1,2
                vertOfs + 1, vertOfs + 2, vertOfs + 3));
            file.WriteLine();

            return vertOfs + 3;
        }

        private static int SaveAABBToOBJ(StreamWriter file, string name, AABB box, int vertOfs)
        {
            if (name != null)
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

        private struct QNode
        {
            public TreeNode T;
            public int level;

            public QNode(TreeNode T, int level)
            {
                this.T = T;
                this.level = level;
            }
        }

        /// <summary>
        /// Saves the given tree to an OBJ file using a bread-first search. This makes one OBJ
        /// object per level.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="root"></param>
        /// <param name="vertOfs"></param>
        /// <param name="vertices"></param>
        /// <param name="indices"></param>
        /// <returns></returns>
        private static int _SaveBVHToOBJ(StreamWriter file, TreeNode root, int vertOfs,
            in List<Vector> vertices, in List<Int32> indices)
        {
            int prev_level = -1;
            Queue<QNode> Q = new Queue<QNode>();
            Q.Enqueue(new QNode(root, 0));
            while (Q.Count > 0)
            {
                QNode qnode = Q.Dequeue();
                TreeNode T = qnode.T;
                // Start a new OBJ object if we jumped to a new level
                if (prev_level != qnode.level)
                {
                    file.WriteLine("o Level-" + qnode.level);
                    prev_level = qnode.level;
                }

                vertOfs = SaveAABBToOBJ(file, null, T.box, vertOfs);
                if (T.IsLeaf())
                    vertOfs = SaveTriangleToOBJ(file, null, T.boxRefIdx, vertOfs, vertices, indices);

                foreach (TreeNode child in T.GetChildren())
                    Q.Enqueue(new QNode(child, qnode.level + 1));
            }
            return vertOfs;
        }

        public static void SaveBVHToOBJ(string sOutFileName, TreeNode T,
            in List<Vector> vertices, in List<Int32> indices)
        {
            StreamWriter file = new StreamWriter(sOutFileName);
            _SaveBVHToOBJ(file, T, 0, vertices, indices);
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
                return new TreeNode(boxes[i].TriID, boxes[i].code);

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

        /// <summary>
        /// Naive implementation of the SBVH minus triangle splitting.
        /// In other words, sort along the longest axis at each level.
        /// </summary>
        /// <param name="boxes">Unsorted AABBs</param>
        /// <param name="leftIdx">Left index of the range to process</param>
        /// <param name="rightIdx">Right index of the range to process</param>
        /// <returns>A binary tree that represents the LBVH. All internal nodes will be missing AABBs,
        /// so this tree must be refit later.</returns>
        private static TreeNode BuildSBVHFast(ref List<BoxRef> boxes, in int leftIdx, in int rightIdx)
        {
            int split_idx = -1;
            if (leftIdx == rightIdx)
                return new TreeNode(boxes[leftIdx].TriID, boxes[leftIdx].box);

            if (leftIdx + 1 == rightIdx)
            {
                AABB box = new AABB();
                box.Expand(boxes[leftIdx].box);
                box.Expand(boxes[rightIdx].box);
                return new TreeNode(-1,
                    box,
                    new TreeNode(boxes[leftIdx].TriID, boxes[leftIdx].box),
                    new TreeNode(boxes[rightIdx].TriID, boxes[rightIdx].box));
            }

            // Get the bounding box for this range
            AABB centroidRangeBox = new AABB();
            AABB rangeBox = new AABB();
            for (int idx = leftIdx; idx <= rightIdx; idx++)
            {
                centroidRangeBox.Expand(boxes[idx].box.GetCentroid());
                rangeBox.Expand(boxes[idx].box);
            }
            XwVector centroidRange = XwVector.Substract(centroidRangeBox.max, centroidRangeBox.min);
            XwVector range = XwVector.Substract(rangeBox.max, rangeBox.min);

#if DISABLED
            Console.WriteLine("i: " + leftIdx + ", j: " + rightIdx + ", rangeBox: " + rangeBox);
            Console.WriteLine("range: " + range);
#endif

            // Find the longest axis
            int axis = -1;
            float max = -1.0f;
            for (int idx = 0; idx < 3; idx++)
                if (centroidRange[idx] > max)
                {
                    max = centroidRange[idx];
                    //max = range[idx];
                    axis = idx;
                }

#if DISABLED
            Console.WriteLine("max: " + max + ", axis: " + comparer.axis);
            Console.WriteLine("Boxes before sorting:");
            for (int i = leftIdx; i <= rightIdx; i++)
            {
                Console.Write(boxes[i].TriID + ":(" + boxes[i].centroid + "), ");
            }
            Console.WriteLine();
#endif
            /*
            // Sort along the maximum axis
            BoxRefComparer comparer = new BoxRefComparer();
            comparer.axis = -1;
            boxes.Sort(leftIdx, rightIdx - leftIdx + 1, comparer);
            split_idx = (leftIdx + rightIdx + 1) / 2;
            */

            // Find the "median point" along the longest axis. I would call this the
            // geometric middle point, but literature disagrees, so whatever.
            float mid = centroidRangeBox.min[axis] + centroidRange[axis] / 2.0f;
            List<BoxRef> leftBoxes = new List<BoxRef>();
            List<BoxRef> rightBoxes = new List<BoxRef>();
            // Classify each box in the interval into either the left or right sub-range
            for (int i = leftIdx; i <= rightIdx; i++)
            {
                BoxRef boxRef = boxes[i];
                XwVector centroid = boxRef.box.GetCentroid();
                if (centroid[axis] <= mid)
                    leftBoxes.Add(boxRef);
                else
                    rightBoxes.Add(boxRef);
            }

            // Check
            if ((leftBoxes.Count + rightBoxes.Count) != (rightIdx - leftIdx + 1))
                throw new Exception("left/right boxes count mismatch");

            split_idx = leftIdx + leftBoxes.Count;

            // Write back the left/right sub ranges into the original boxes List
            int destIdx = leftIdx;
            foreach (BoxRef boxRef in leftBoxes)
                boxes[destIdx++] = boxRef;
            foreach (BoxRef boxRef in rightBoxes)
                boxes[destIdx++] = boxRef;

            // Check
            if (destIdx != rightIdx + 1)
                throw new Exception("destIdx is in the wrong position after the writeback");

#if DISABLED
            Console.WriteLine("Boxes after sorting:");
            for (int i = leftIdx; i <= rightIdx; i++)
            {
                Console.Write(boxes[i].TriID + ":(" + boxes[i].centroid + "), ");
            }
            Console.WriteLine();
            Console.WriteLine("split_idx: " + split_idx);
#endif

            return new TreeNode(
                -1,
                rangeBox,
                BuildSBVHFast(ref boxes, leftIdx, split_idx - 1),
                BuildSBVHFast(ref boxes, split_idx, rightIdx)
            );
        }

        private static TreeNode BuildSBVHStable(ref List<BoxRef> boxes, in int leftIdx, in int rightIdx)
        {
            int split_idx = -1;
            if (leftIdx == rightIdx)
                return new TreeNode(boxes[leftIdx].TriID, boxes[leftIdx].box);

            if (leftIdx + 1 == rightIdx)
            {
                AABB box = new AABB();
                box.Expand(boxes[leftIdx].box);
                box.Expand(boxes[rightIdx].box);
                return new TreeNode(-1,
                    box,
                    new TreeNode(boxes[leftIdx].TriID, boxes[leftIdx].box),
                    new TreeNode(boxes[rightIdx].TriID, boxes[rightIdx].box));
            }

            // Get the bounding box for this range
            AABB centroidRangeBox = new AABB();
            AABB rangeBox = new AABB();
            for (int idx = leftIdx; idx <= rightIdx; idx++)
            {
                centroidRangeBox.Expand(boxes[idx].box.GetCentroid());
                rangeBox.Expand(boxes[idx].box);
            }
            XwVector centroidRange = XwVector.Substract(centroidRangeBox.max, centroidRangeBox.min);
            XwVector range = XwVector.Substract(rangeBox.max, rangeBox.min);

            // Find the longest axis
            int axis = -1;
            float max = -1.0f;
            for (int idx = 0; idx < 3; idx++)
                if (centroidRange[idx] > max)
                {
                    //max = centroidRange[idx];
                    max = range[idx];
                    axis = idx;
                }

            // Sort along the maximum axis
            BoxRefComparer comparer = new BoxRefComparer();
            comparer.axis = axis;
            boxes.Sort(leftIdx, rightIdx - leftIdx + 1, comparer);
            split_idx = (leftIdx + rightIdx + 1) / 2;

            return new TreeNode(
                -1,
                rangeBox,
                BuildSBVHStable(ref boxes, leftIdx, split_idx - 1),
                BuildSBVHStable(ref boxes, split_idx, rightIdx)
            );
        }
   
#if DISABLED
        private static TreeNode BuildSBVH(ref List<BoxRef> boxes)
        {
            if (boxes.Count == 1)
                return new TreeNode(boxes[0].TriID, boxes[0].box);

            if (boxes.Count == 2)
            {
                AABB box = new AABB();
                box.Expand(boxes[0].box);
                box.Expand(boxes[1].box);
                return new TreeNode(-1,
                    box,
                    new TreeNode(boxes[0].TriID, boxes[0].box),
                    new TreeNode(boxes[1].TriID, boxes[1].box));
            }

            // Get the bounding box for this range
            AABB rangeBox = new AABB();
            for (int i = 0; i <= boxes.Count; i++)
                rangeBox.Expand(boxes[i].box.GetCentroid());
            XwVector range = XwVector.Substract(rangeBox.max, rangeBox.min);

            // Find the longest axis
            //BoxRefComparer comparer = new BoxRefComparer();
            //comparer.axis = -1;
            int axis = -1;
            float max = -1.0f;
            for (int idx = 0; idx < 3; idx++)
                if (range[idx] > max)
                {
                    max = range[idx];
                    //comparer.axis = idx;
                    axis = idx;
                }

            // Sort along the maximum axis
            //boxes.Sort(leftIdx, rightIdx - leftIdx + 1, comparer);
            // Get the split position:
            //split_idx = (leftIdx + rightIdx) / 2;

            // Find the "median point" along the longest axis. I would call this the
            // geometric middle point, but literature disagrees, so whatever.
            float mid = rangeBox.min[axis] + range[axis] / 2.0f;
            List<BoxRef> leftBoxes = new List<BoxRef>();
            List<BoxRef> rightBoxes = new List<BoxRef>();
            // Classify each box in the interval into either the left or right sub-range
            for (int i = leftIdx; i <= rightIdx; i++)
            {
                BoxRef boxRef = boxes[i];
                XwVector centroid = boxRef.box.GetCentroid();
                if (centroid[axis] <= mid)
                    leftBoxes.Add(boxRef);
                else
                    rightBoxes.Add(boxRef);
            }

            // Check
            if ((leftBoxes.Count + rightBoxes.Count) != (rightIdx - leftIdx + 1))
                throw new Exception("left/right boxes count mismatch");

            int split_idx = leftIdx + leftBoxes.Count;

            // Write back the left/right sub ranges into the original boxes List
            int destIdx = leftIdx;
            foreach (BoxRef boxRef in leftBoxes)
                boxes[destIdx++] = boxRef;
            foreach (BoxRef boxRef in rightBoxes)
                boxes[destIdx++] = boxRef;

            // Check
            if (destIdx != rightIdx + 1)
                throw new Exception("destIdx is in the wrong position after the writeback");

            return new TreeNode(
                -1,
                rangeBox,
                BuildSBVH(ref boxes, leftIdx, split_idx - 1),
                BuildSBVH(ref boxes, split_idx, rightIdx)
            );
        }
#endif

        public static void ComputeBVH(string sInFileName, string sOutFileName, out string sError)
        {
            var opt = OptFile.FromFile(sInFileName);
            Console.WriteLine("Loaded " + sInFileName);
            List<Vector> Vertices = new List<Vector>();
            List<Int32> Indices = new List<int>();
            List<BoxRef> Boxes = new List<BoxRef>();
            List<AABB> MeshAABBs = new List<AABB>();
            List<Int32> VertexCounts = new List<Int32>();
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
                AABB meshAABB = new AABB();

                // Remove all the LODs, except for the first one (we'll handle LODs later)
                for (int i = 1; i < mesh.Lods.Count; i++)
                    mesh.Lods.RemoveAt(i);

                // Copy the vertices to their final destination
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    Vertices.Add(mesh.Vertices[i]);

                // Update this mesh's AABB, and add it to the list
                foreach (var V in Vertices)
                    meshAABB.Expand(V);
                MeshAABBs.Add(meshAABB);
                // Add the vertex count as well, meshIdx and Vertices.Count will be used
                // to identify this mesh
                VertexCounts.Add(mesh.Vertices.Count);
                //Console.WriteLine("meshIdx: " + meshIdx + ", Vertices.Count: " + VertexCounts.Last());

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
                            boxref.TriID = TriId++;
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
                                boxref.TriID = TriId++;
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

            int numPrims = Boxes.Count;
            Console.WriteLine("numPrims: " + numPrims);

            // Build the tree proper
            //TreeNode T = BuildLBVH(Boxes, 0, numPrims - 1);
            // Compute the inner nodes' AABBs
            // TODO: The refit is buggy: it's messing up the inner nodes AABBs for some reason
            //int NumNodes = 0;
            //Refit(T, Boxes, out NumNodes);

            TreeNode T = BuildSBVHStable(ref Boxes, 0, numPrims - 1);
            int NumNodes = CountNodes(T);
#if DEBUG
            //PrintTree("", T);
#endif
            // DEBUG, let's dump the BVH tree structure
            SaveBVHToOBJ("c:\\temp\\LBVH.obj", T, Vertices, Indices);
            //SaveOBJ("c:\\Temp\\LBVHInput-after-sort.obj", Vertices, Indices);

            // Save the tree and the primitives
            SaveBVH(sOutFileName, NumNodes, T, Vertices, Indices,
                MeshAABBs, VertexCounts, out sError);
            Console.WriteLine("Saved: " + sOutFileName);

            // Tidy up
            DeleteTree(T);
            T = null;
        }

        private static void SaveBVH(string sOutFileName, int NumNodes, TreeNode T,
            List<Vector> Vertices, List<int> Indices,
            List<AABB> MeshAABBs, List<Int32> VertexCounts,
            out string sError)
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
                UInt32 NumVertices = (UInt32)Vertices.Count;
                file.Write(NumVertices);

#if DEBUG
                Console.WriteLine("NumVertices: " + NumVertices);
                Console.WriteLine(String.Format("Vertex[0]: {0}, {1}, {2}",
                    Vertices[0].X, Vertices[0].Y, Vertices[0].Z));
#endif
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

#if DEBUG
                Console.WriteLine("NumIndices: " + NumIndices);
                Console.WriteLine(String.Format("Indices[0,1,2]: {0}, {1}, {2}",
                    Indices[0], Indices[1], Indices[2]));
#endif

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
                SaveLBVH(file, NumNodes, T);
            }

            // Save the mesh AABBs
            {
                UInt32 NumMeshes = (UInt32)MeshAABBs.Count;
                file.Write(NumMeshes);

                for (int i = 0; i < MeshAABBs.Count; i++)
                {
                    file.Write(BVHEncoder.EncodeAABB(MeshAABBs[i]));
                }
            }

            // Save the vertex counts
            {
                UInt32 NumVertexCounts = (UInt32)VertexCounts.Count;
                file.Write(NumVertexCounts);
                for (int i = 0; i < VertexCounts.Count; i++)
                {
                    file.Write(VertexCounts[i]);
                }
            }

            file.Close();
        }

        private static void SaveLBVH(BinaryWriter file, int NumNodes, IGenericTree root)
        {
            if (root == null)
                return;

            // Write the number of nodes in the tree
            file.Write(NumNodes);

#if DEBUG
            {
                Console.WriteLine(String.Format("NumNodes: {0}", NumNodes));
                TreeNode T = (TreeNode)root;

                Console.WriteLine(String.Format("root: ref: {0}", T.boxRefIdx));
                Console.WriteLine(String.Format("min: {0},{1},{2}",
                    T.box.min.x, T.box.min.y, T.box.min.z));
                Console.WriteLine(String.Format("max: {0},{1},{2}",
                    T.box.max.x, T.box.max.y, T.box.max.z));

                T = T.left;
                if (T != null)
                {
                    Console.WriteLine("Left child: ");
                    Console.WriteLine(String.Format("min: {0},{1},{2}",
                        T.box.min.x, T.box.min.y, T.box.min.z));
                    Console.WriteLine(String.Format("max: {0},{1},{2}",
                        T.box.max.x, T.box.max.y, T.box.max.z));
                }
            }
#endif
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
                    BVHEncoder.EncodeTreeNode((TreeNode)T, 0 /* parent (TODO) */,
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
