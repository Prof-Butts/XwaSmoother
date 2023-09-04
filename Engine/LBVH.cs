//#define CENTROID_SPLIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

using JeremyAnsel.Xwa.Opt;
using XwaOpter;

namespace XwaSmootherEngine
{
    using MortonCode_t = UInt32;

    public class AABB
    {
        public XwVector min, max;

        public AABB()
        {
            this.min = new XwVector();
            this.max = new XwVector();
            MakeInvalid();
        }

        public AABB(AABB box)
        {
            this.min = new XwVector();
            this.max = new XwVector();
            for (int i = 0; i < 3; i++)
            {
                this.min[i] = box.min[i];
                this.max[i] = box.max[i];
            }
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

        public void Expand(AABB box)
        {
            if (box == null)
                return;

            for (int i = 0; i < 3; i++)
            {
                if (box.min[i] < this.min[i]) this.min[i] = box.min[i];
                if (box.max[i] > this.max[i]) this.max[i] = box.max[i];
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
        // BVHNode4 class that looks like a C union. I ended up not needing this after all,
        // but it's nice to know how it's done.
#if DISABLED
        [System.Runtime.InteropServices.StructLayout(LayoutKind.Explicit)]
        public class BVHNode4
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public Int32 TriID;
            [System.Runtime.InteropServices.FieldOffset(4)]
            public Int32 parent;

            [System.Runtime.InteropServices.FieldOffset(8)]
            public float v0x;
            [System.Runtime.InteropServices.FieldOffset(12)]
            public float v0y;
            [System.Runtime.InteropServices.FieldOffset(16)]
            public float v0z;

            [System.Runtime.InteropServices.FieldOffset(20)]
            public float v1x;
            [System.Runtime.InteropServices.FieldOffset(24)]
            public float v1y;
            [System.Runtime.InteropServices.FieldOffset(28)]
            public float v1z;

            [System.Runtime.InteropServices.FieldOffset(32)]
            public Int32 child0;
            [System.Runtime.InteropServices.FieldOffset(32)]
            public float v2x;

            [System.Runtime.InteropServices.FieldOffset(36)]
            public Int32 child1;
            [System.Runtime.InteropServices.FieldOffset(36)]
            public float v2y;

            [System.Runtime.InteropServices.FieldOffset(40)]
            public Int32 child2;
            [System.Runtime.InteropServices.FieldOffset(40)]
            public float v2z;

            [System.Runtime.InteropServices.FieldOffset(44)]
            public Int32 child3;
        }
#endif

        // In the encoded BVH nodes we can either store triangle indices, or the triangle vertices
        // themselves. If we store indices, we're probably going to have a smaller tree, but lower
        // performance because we need to do multiple memory reads.
        // If we store the geometry in the BVH leaves, we get a bigger tree, but better performance.

        // Also, when loading multiple OPTs, it's probably easier to embed the geometry in the tree.
        // That way we don't have to worry about indices referencing vertices in other buffers.

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

        public static unsafe int EncodeVector(byte* dst, int ofs, Vector V)
        {
            ofs = EncodeFloat(dst, ofs, V.X);
            ofs = EncodeFloat(dst, ofs, V.Y);
            ofs = EncodeFloat(dst, ofs, V.Z);
            return ofs;
        }

        public static unsafe int EncodeVector4(byte* dst, int ofs, Vector V)
        {
            float w = 1.0f;
            ofs = EncodeFloat(dst, ofs, V.X);
            ofs = EncodeFloat(dst, ofs, V.Y);
            ofs = EncodeFloat(dst, ofs, V.Z);
            ofs = EncodeFloat(dst, ofs, w);
            return ofs;
        }

        public static unsafe byte[] EncodeAABB(AABB aabb)
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

        public static unsafe byte[] EncodeTreeNode2(TreeNode T, Int32 parent, Int32 left, Int32 right)
        {
            byte[] data = new byte[LBVH.ENCODED_TREE_NODE_SIZE_BVH2];
            int ofs = 0;
            fixed (byte *dst = &data[0])
            {
                ofs = EncodeInt32(dst, ofs, T.TriID);
                ofs = EncodeInt32(dst, ofs, left);
                ofs = EncodeInt32(dst, ofs, right);
                ofs = EncodeInt32(dst, ofs, parent);
                // 16 bytes
                ofs = EncodeXwVector4(dst, ofs, T.box.min);
                // 16 bytes
                ofs = EncodeXwVector4(dst, ofs, T.box.max);
                // 16 bytes
            }

            if (ofs != LBVH.ENCODED_TREE_NODE_SIZE_BVH2)
            {
                throw new Exception("TreeNode should be encoded in " + LBVH.ENCODED_TREE_NODE_SIZE_BVH2 +
                    " bytes, got " + ofs + " instead");
            }
            return data;
        }

        /// <summary>
        /// Encode a BVH4 node using either Indexed or Embedded Geometry.
        /// </summary>
        public static unsafe byte[] EncodeTreeNode4(IGenericTree T, Int32 parent, List<Int32> children,
            bool EmbedVertices, List<Vector> Vertices, List<Int32> Indices)
        {
            byte[] data = new byte[LBVH.ENCODED_TREE_NODE_SIZE_BVH4];
            AABB box = T.GetBox();
            int TriID = T.GetTriID();
            int padding = 0;
            int ofs = 0;

            // This leaf node must have its vertices embedded in the node
            if (EmbedVertices && TriID != -1)
            {
                int vertofs = TriID * 3;
                Vector v0 = Vertices[Indices[vertofs]];
                Vector v1 = Vertices[Indices[vertofs + 1]];
                Vector v2 = Vertices[Indices[vertofs + 2]];

                fixed (byte* dst = &data[0])
                {
                    ofs = EncodeInt32(dst, ofs, TriID);
                    ofs = EncodeInt32(dst, ofs, parent);
                    ofs = EncodeInt32(dst, ofs, padding);
                    ofs = EncodeInt32(dst, ofs, padding);
                    // 16 bytes 

                    ofs = EncodeVector4(dst, ofs, v0);
                    // 32 bytes
                    ofs = EncodeVector4(dst, ofs, v1);
                    // 48 bytes
                    ofs = EncodeVector4(dst, ofs, v2);
                    // 64 bytes
                }
            }
            else
            {
                fixed (byte* dst = &data[0])
                {
                    ofs = EncodeInt32(dst, ofs, TriID);
                    ofs = EncodeInt32(dst, ofs, parent);
                    ofs = EncodeInt32(dst, ofs, padding);
                    ofs = EncodeInt32(dst, ofs, padding);
                    // 16 bytes
                    ofs = EncodeXwVector4(dst, ofs, box.min);
                    // 32 bytes
                    ofs = EncodeXwVector4(dst, ofs, box.max);
                    // 48 bytes
                    for (int i = 0; i < 4; i++)
                        ofs = EncodeInt32(dst, ofs, children[i]);
                    // 64 bytes
                }
            }

            if (ofs != LBVH.ENCODED_TREE_NODE_SIZE_BVH4)
            {
                throw new Exception("TreeNode should be encoded in " + LBVH.ENCODED_TREE_NODE_SIZE_BVH4 +
                    " bytes, got " + ofs + " instead");
            }
            return data;
        }
    }

    public interface IGenericTree
    {
        int GetArity();
        bool IsLeaf();
        AABB GetBox();
        int GetTriID();
        List<IGenericTree> GetChildren();
        IGenericTree GetParent();
        void SetNumNodes(int numNodes);
        int GetNumNodes();
    }

    public class TreeNode : IGenericTree
    {
        public int TriID, numNodes;
        public TreeNode left, right, parent;
        public AABB box;
        public MortonCode_t code;

        public TreeNode()
        {
            TriID = -1;
            left = right = parent = null;
            box = new AABB();
            code = 0;
            numNodes = 0;
        }

        public TreeNode(int TriID)
        {
            this.TriID = TriID;
            left = right = parent = null;
            box = new AABB();
            code = 0;
            numNodes = 0;
        }

        public TreeNode(int TriID, MortonCode_t code)
        {
            this.TriID = TriID;
            this.code = code;
            left = right = parent = null;
            this.box = new AABB();
            this.numNodes = 0;
        }

        public TreeNode(int TriID, TreeNode left, TreeNode right)
        {
            this.TriID = TriID;
            this.left = left;
            this.right = right;
            this.parent = null;
            this.box = new AABB();
            this.code = 0;
            this.numNodes = 0;
        }

        public TreeNode(int TriID, AABB box, TreeNode left, TreeNode right)
        {
            this.TriID = TriID;
            this.left = left;
            this.right = right;
            this.parent = null;
            this.box = box;
            this.code = 0;
            this.numNodes = 0;
        }

        public TreeNode(int TriID, TreeNode left, TreeNode right, TreeNode parent)
        {
            this.TriID = TriID;
            this.left = left;
            this.right = right;
            this.parent = parent;
            this.box = new AABB();
            this.code = 0;
            this.numNodes = 0;
        }

        public TreeNode(int TriID, AABB box)
        {
            this.TriID = TriID;
            this.left = null;
            this.right = null;
            this.parent = null;
            this.code = 0;
            this.box = new AABB();
            this.box.Expand(box);
            this.numNodes = 0;
        }

        public int GetArity()
        {
            return 2;
        }

        public AABB GetBox()
        {
            return this.box;
        }

        public int GetTriID()
        {
            return this.TriID;
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

        public void SetNumNodes(int numNodes)
        {
            this.numNodes = numNodes;
        }

        public int GetNumNodes()
        {
            return this.numNodes;
        }
    }

    public class QTreeNode : IGenericTree
    {
        public int TriID, numNodes;
        public QTreeNode parent;
        public QTreeNode[] children;
        public AABB box;
        public MortonCode_t code;

        public QTreeNode(int TriID)
        {
            this.TriID = TriID;
            this.box = null;
            this.code = 0;
            this.children = new QTreeNode[4];
            this.numNodes = 0;
            for (int i = 0; i < 4; i++)
                this.children[i] = null;
        }

        public QTreeNode(int TriID, QTreeNode[] children)
        {
            this.TriID = TriID;
            this.box = null;
            this.code = 0;
            this.children = new QTreeNode[4];
            this.numNodes = 0;
            for (int i = 0; i < 4; i++)
                this.children[i] = children[i];
        }

        public QTreeNode(int TriID, AABB box)
        {
            this.TriID = TriID;
            this.children = new QTreeNode[4];
            this.box = box;
            this.code = 0;
            this.numNodes = 0;
            for (int i = 0; i < 4; i++)
                this.children[i] = null;
        }

        public QTreeNode(int TriID, AABB box, QTreeNode[] children, QTreeNode parent)
        {
            this.TriID = TriID;
            this.box = box;
            this.parent = parent;
            this.children = new QTreeNode[4];
            this.numNodes = 0;
            for (int i = 0; i < 4; i++)
                this.children[i] = children[i];
        }

        public int GetArity()
        {
            return 4;
        }

        public AABB GetBox()
        {
            return this.box;
        }

        public int GetTriID()
        {
            return this.TriID;
        }

        public bool IsLeaf()
        {
            for (int i = 0; i < 4; i++)
                if (children[i] != null)
                    return false;
            return true;
        }

        public List<IGenericTree> GetChildren()
        {
            List<IGenericTree> result = new List<IGenericTree>();
            for (int i = 0; i < 4; i++)
                if (children[i] != null)
                    result.Add(children[i]);
            return result;
        }

        public IGenericTree GetParent()
        {
            return parent;
        }

        public void SetNumNodes(int numNodes)
        {
            this.numNodes = numNodes;
        }

        public int GetNumNodes()
        {
            return this.numNodes;
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
        public const int ENCODED_TREE_NODE_SIZE_BVH2 = 48;
        public const int ENCODED_TREE_NODE_SIZE_BVH4 = 64;
        public const bool g_EmbedVertices = true;
        /// <summary>
        /// When true, build one BLAS per mesh and write them together in a single
        /// .bvh file. The header for this type of file encodes the number of trees
        /// in the file.
        /// </summary>
        //public static bool g_BuildMultiBLAS = true;
        public static bool g_BuildMultiBLAS = false;

        public enum BuilderType
        {
            LBVH,
            SBVH
        };
        public static BuilderType g_Builder = BuilderType.LBVH;

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

        private static Vector Sub(Vector A, Vector B)
        {
            Vector C = new Vector();
            C.X = A.X - B.X;
            C.Y = A.Y - B.Y;
            C.Z = A.Z - B.Z;
            return C;
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

        private static void Div(ref Vector Numerator, float Denominator)
        {
            Numerator.X /= Denominator;
            Numerator.Y /= Denominator;
            Numerator.Z /= Denominator;
        }

        private static float Length(Vector V)
        {
            return (float)Math.Sqrt(V.X * V.X + V.Y * V.Y + V.Z * V.Z);
        }

        private static void Normalize(ref Vector V)
        {
            float L = Length(V);
            Div(ref V, L);
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
        public static MortonCode_t GetMortonCode32(uint x, uint y, uint z)
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

        private static void PrintTree(string level, TreeNode T)
        {
            if (T == null)
                return;
            PrintTree(level + "    ", T.right);
            Console.WriteLine(level + T.TriID);
            /*
            if (T.boxRefIdx == -1)
                Console.WriteLine(level + T.boxRefIdx);
            else
                Console.WriteLine(level + T.boxRefIdx + "-" + to_binary(T.code, 3));
            */
            PrintTree(level + "    ", T.left);
        }

        private static void PrintTree(string level, IGenericTree T)
        {
            if (T == null)
                return;

            int arity = T.GetArity();
            List<IGenericTree> children = T.GetChildren();

            Console.WriteLine(level + "    --");
            for (int i = arity - 1; i >= arity / 2; i--)
                if (i < children.Count)
                    PrintTree(level + "    ", children[i]);
            
            Console.WriteLine(level + T.GetTriID());

            for (int i = arity / 2 - 1; i >= 0; i--)
                if (i < children.Count)
                    PrintTree(level + "    ", children[i]);
            Console.WriteLine(level + "    --");
        }

        public static QTreeNode BinTreeToQTree(TreeNode T)
        {
            QTreeNode[] children = { null, null, null, null };
            if (T == null)
                return null;

            if (T.IsLeaf())
                return new QTreeNode(T.TriID, T.box);

            // In a standard BVH all internal nodes have two children, but in BVHs with
            // embedded geometry, the immediate parents of leaves only have one child
            // (the left one). So, the right child may be null in this case.
            TreeNode L = T.left;
            TreeNode R = T.right;
            int nextchild = 0;

            if (L != null)
            {
                if (L.IsLeaf())
                    children[nextchild++] = new QTreeNode(L.TriID, L.box);
                else
                {
                    if (L.left != null)
                        if (L.left.IsLeaf())
                            children[nextchild++] = new QTreeNode(L.left.TriID, L.left.box);
                        else
                            children[nextchild++] = BinTreeToQTree(L.left);

                    if (L.right != null)
                        if (L.right.IsLeaf())
                            children[nextchild++] = new QTreeNode(L.right.TriID, L.right.box);
                        else
                            children[nextchild++] = BinTreeToQTree(L.right);
                }
            }

            if (R != null)
            {
                if (R.IsLeaf())
                    children[nextchild++] = new QTreeNode(R.TriID, R.box);
                else
                {
                    if (R.left != null)
                        if (R.left.IsLeaf())
                            children[nextchild++] = new QTreeNode(R.left.TriID, R.left.box);
                        else
                            children[nextchild++] = BinTreeToQTree(R.left);

                    if (R.right != null)
                        if (R.right.IsLeaf())
                            children[nextchild++] = new QTreeNode(R.right.TriID, R.right.box);
                        else
                            children[nextchild++] = BinTreeToQTree(R.right);
                }
            }

            return new QTreeNode(-1, children);
        }

        private static AABB Refit(TreeNode T, out int NumTreeNodes)
        {
            if (T == null)
            {
                NumTreeNodes = 0;
                return null;
            }

            if (T.IsLeaf())
            {
                // Sorting the boxes breaks the correspondence between a box's index and a triangle
                // index. In other words T.TriID != boxes[T.TriID].TriID
                NumTreeNodes = 1;
                return T.box;
            }

            int NumNodesL, NumNodesR;
            AABB boxL = Refit(T.left, out NumNodesL);
            AABB boxR = Refit(T.right, out NumNodesR);
            T.box = new AABB();
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

        private static AABB Refit(QTreeNode T, out int NumTreeNodes)
        {
            if (T == null)
            {
                NumTreeNodes = 0;
                return null;
            }

            if (T.IsLeaf())
            {
                // Sorting the boxes breaks the correspondence between a box's index and a triangle
                // index. In other words T.TriID != boxes[T.TriID].TriID
                NumTreeNodes = 1;
                return T.box;
            }

            T.box = new AABB();
            int NumNodesChildren = 0;
            for (int i = 0; i < 4; i++)
                if (T.children[i] != null)
                {
                    int NumNodes = 0;
                    T.box.Expand(Refit(T.children[i], out NumNodes));
                    NumNodesChildren += NumNodes;
                    T.children[i].parent = T;
                }

            NumTreeNodes = 1 + NumNodesChildren;
            return T.box;
        }

        private static int CountNodes(TreeNode T)
        {
            if (T == null)
                return 0;

            if (T.TriID != -1)
                return 1;

            return 1 + CountNodes(T.left) + CountNodes(T.right);
        }

        public static float Get(Vector V, int key)
        {
            switch (key)
            {
                case 0: return V.X;
                case 1: return V.Y;
                case 2: return V.Z;
            }
            return float.NaN;
        }

        /// <summary>
        /// Splits a triangle by the given split plane and returns two new split AABBs.
        /// </summary>
        /// <param name="TriID">The triangle ID to split</param>
        /// <param name="box">The (split) AABB of the triangle. This box may not cover
        /// the whole span of the triangle, but it must intersect it and the split plane
        /// must be inside this box. This box can be itself the result of a previous
        /// split.</param>
        /// <param name="axis">The axis of the split</param>
        /// <param name="splitPlane">The position of the split along axis.</param>
        /// <param name="Vertices"></param>
        /// <param name="Indices"></param>
        /// <param name="boxL">The resulting left split box</param>
        /// <param name="boxR">The resulting right split box</param>
        private static void SplitTriangle(int TriID, AABB box, int axis, float splitPlane,
            List<Vector> Vertices, List<Int32> Indices,
            out AABB boxL, out AABB boxR)
        {
            boxL = new AABB();
            boxR = new AABB();

            // Fetch the vertices of this triangle
            List<Vector> triVerts = new List<Vector>();
            int ofs = TriID * 3;
            triVerts.Add(Vertices[Indices[ofs++]]);
            triVerts.Add(Vertices[Indices[ofs++]]);
            triVerts.Add(Vertices[Indices[ofs++]]);

            // Initialize the new split boxes
            foreach (Vector v in triVerts)
            {
                if (Get(v, axis) <= splitPlane)
                    boxL.Expand(v);
                if (Get(v, axis) >= splitPlane)
                    boxR.Expand(v);
            }

            // Intersect each edge in the triangle against the split plane and add
            // the intersections to both boxes
            for (int i = 0; i < 3; i++)
            {
                // Get the intersection for the v0 -> v1 edge
                Vector v0 = triVerts[i];
                Vector v1 = triVerts[(i + 1) % 3];

                // Equation of the ray starting at v0 in the direction of v1:
                //
                //    p = v0 + t * dir
                //
                // where dir = v1 - v0
                //
                // The equation is the same on each component, so the axis is implicit.
                // We know p, that's splitPlane along the given split axis, so we have:
                //
                //    splitPlane = v0[axis] + t * dir[axis]
                //
                // Solving for t:
                //
                //    t = (splitPlane - v0[axis]) / dir[axis]
                //
                // Then we just plug t back in the original equation to get the intersection
                Vector dir = Sub(v1, v0);
                float t = (splitPlane - Get(v0, axis)) / Get(dir, axis);
                Vector p = new Vector(v0.X, v0.Y, v0.Z);
                Mul(t, ref dir);
                Add(ref p, dir);
                // The intersection is within the current edge iff t is in the range [0..1]
                if (t >= 0.0f && t <= 1.0f)
                {
                    boxL.Expand(p);
                    boxR.Expand(p);
                }
            }

            // At this point, boxL and boxR should be tight boxes around the triangle and the
            // split plane. But maybe the input box itself is the product of a previous split.
            // Therefore now we need to shrink the new boxes so that they aren't bigger than
            // the initial box
            for (int i = 0; i < 3; i++) {
                boxL.min[i] = Math.Max(boxL.min[i], box.min[i]);
                boxL.max[i] = Math.Min(boxL.max[i], box.max[i]);

                boxR.min[i] = Math.Max(boxR.min[i], box.min[i]);
                boxR.max[i] = Math.Min(boxR.max[i], box.max[i]);
            }
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
            List<Vector> Vertices, List<int> Indices)
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
            public IGenericTree T;
            public int level;

            public QNode(IGenericTree T, int level)
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
        private static int _SaveBVHToOBJ(StreamWriter file, IGenericTree root, int vertOfs,
            List<Vector> vertices, List<Int32> indices, bool onlyLeaves=false)
        {
            int prev_level = -1;
            Queue<QNode> Q = new Queue<QNode>();
            Q.Enqueue(new QNode(root, 0));
            while (Q.Count > 0)
            {
                QNode qnode = Q.Dequeue();
                IGenericTree T = qnode.T;
                // Start a new OBJ object if we jumped to a new level
                if (prev_level != qnode.level)
                {
                    file.WriteLine("o Level-" + qnode.level);
                    prev_level = qnode.level;
                }

                if (onlyLeaves)
                {
                    if (T.IsLeaf())
                    {
                        vertOfs = SaveAABBToOBJ(file, null, T.GetBox(), vertOfs);
                        vertOfs = SaveTriangleToOBJ(file, null, T.GetTriID(), vertOfs, vertices, indices);
                    }
                }
                else
                {
                    vertOfs = SaveAABBToOBJ(file, null, T.GetBox(), vertOfs);
                    if (T.IsLeaf())
                        vertOfs = SaveTriangleToOBJ(file, null, T.GetTriID(), vertOfs, vertices, indices);
                }

                foreach (IGenericTree child in T.GetChildren())
                    Q.Enqueue(new QNode(child, qnode.level + 1));
            }
            return vertOfs;
        }

        public static void SaveBVHToOBJ(string sOutFileName, IGenericTree T,
            List<Vector> vertices, List<Int32> indices, bool onlyLeaves=false)
        {
            StreamWriter file = new StreamWriter(sOutFileName);
            _SaveBVHToOBJ(file, T, 0, vertices, indices, onlyLeaves);
            file.Close();
        }

        private static TreeNode BuildBinHeap(int level, List<BoxRef> boxes, int i, int j /*, AABB sceneBox */)
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
        private static TreeNode BuildLBVH(List<BoxRef> boxes, int i, int j)
        {
            int split_idx = -1;

            // Indexed Geometry
            /*
            if (i == j)
                return new TreeNode(boxes[i].TriID, boxes[i].code);
            */

            // Embedded Geometry
            // It may seem redundant to have an inner node with the same box as the leaf,
            // but when this tree gets encoded, the leaf node has its box replaced with
            // the embedded vertices. Also, we need the leaves to have the right boxes for
            // the refit step.
            if (i == j)
                return new TreeNode(-1, boxes[i].box,
                        new TreeNode(boxes[i].TriID, boxes[i].box), null);

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
        /// </summary>
        /// <param name="boxes">Unsorted AABBs</param>
        /// <param name="leftIdx">Left index of the range to process</param>
        /// <param name="rightIdx">Right index of the range to process</param>
        /// <returns>A binary tree that represents the BVH, no refitting needed.</returns>
        private static TreeNode BuildSBVHFast(ref List<BoxRef> boxes, int leftIdx, int rightIdx)
        {
            int split_idx = -1;
            // Indexed (i.e. non-embedded) Geometry:
            /*
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
            */

            // Embedded Geometry:
            // It may seem redundant to have an inner node with the same box as the leaf,
            // but when this tree gets encoded, the leaf node has its box replaced with
            // the embedded vertices. Also, we need the leaves to have the right boxes for
            // the refit step.
            if (leftIdx == rightIdx)
                return new TreeNode(-1, boxes[leftIdx].box,
                    new TreeNode(boxes[leftIdx].TriID, boxes[leftIdx].box), null);

            if (leftIdx + 1 == rightIdx)
            {
                AABB box = new AABB();
                box.Expand(boxes[leftIdx].box);
                box.Expand(boxes[rightIdx].box);
                return new TreeNode(-1,
                    box,
                    new TreeNode(-1, boxes[leftIdx].box,
                        new TreeNode(boxes[leftIdx].TriID, boxes[leftIdx].box), null),
                    new TreeNode(-1, boxes[rightIdx].box,
                        new TreeNode(boxes[rightIdx].TriID, boxes[rightIdx].box), null)
                );
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
#if CENTROID_SPLIT
                if (centroidRange[idx] > max)
#else
                if (range[idx] > max)
#endif
                {
#if CENTROID_SPLIT
                    max = centroidRange[idx];
#else
                    max = range[idx];
#endif
                    axis = idx;
                }

            // Sort along the maximum axis
            BoxRefComparer comparer = new BoxRefComparer();
            comparer.axis = axis;
            boxes.Sort(leftIdx, rightIdx - leftIdx + 1, comparer);
            // Compute the binned SAH to find a better split...
            int mid_idx = (leftIdx + rightIdx + 1) / 2;

            // Find the "median point" along the longest axis. I would call this the
            // geometric middle point, but literature disagrees, so whatever.
#if CENTROID_SPLIT
            float mid = centroidRangeBox.min[axis] + centroidRange[axis] / 2.0f;
#else
            float mid = rangeBox.min[axis] + range[axis] / 2.0f;
#endif
            List<BoxRef> leftBoxes = new List<BoxRef>();
            List<BoxRef> rightBoxes = new List<BoxRef>();
            // Classify each box in the interval into either the left or right sub-range
            for (int i = leftIdx; i <= rightIdx; i++)
            {
                BoxRef boxRef = boxes[i];
                XwVector centroid = boxRef.box.GetCentroid();
#if CENTROID_SPLIT
                if (centroid[axis] <= mid)
                    leftBoxes.Add(boxRef);
                else
                    rightBoxes.Add(boxRef);
#else
                if (boxRef.box.max[axis] < mid)
                    leftBoxes.Add(boxRef);
                else if (boxRef.box.min[axis] > mid)
                    rightBoxes.Add(boxRef);
                else
                {
                    // Primitive straddles the split plane, either split the primitive or use
                    // its index to decide where to send it
                    if (i < mid_idx)
                        leftBoxes.Add(boxRef);
                    else
                        rightBoxes.Add(boxRef);
                }
#endif
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

            return new TreeNode(
                -1,
                rangeBox,
                BuildSBVHFast(ref boxes, leftIdx, split_idx - 1),
                BuildSBVHFast(ref boxes, split_idx, rightIdx)
            );
        }

        private static TreeNode BuildSBVHStable(ref List<BoxRef> boxes, int leftIdx, int rightIdx)
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

#if DEBUG
            // Checkpoint: save an OBJ file and check that everything looks fine
            SaveOBJ("c:\\Temp\\LBVHInput.obj", Vertices, Indices);
#endif

            // Sort the primitives by their Morton codes
            Boxes.Sort();

            int numPrims = Boxes.Count;
            Console.WriteLine("numPrims: " + numPrims);

            // Build the tree proper
            //TreeNode T = BuildLBVH(Boxes, 0, numPrims - 1);
            // Compute the inner nodes' AABBs
            //int NumNodes = 0;
            //Refit(T, out NumNodes);

            TreeNode T = null;
            switch (g_Builder) {
                case BuilderType.LBVH:
                    Console.WriteLine("Building LBVH");
                    T = BuildLBVH(Boxes, 0, numPrims - 1);
                    break;
                case BuilderType.SBVH:
                    Console.WriteLine("Building SBVH");
                    //T = BuildSBVHStable(ref Boxes, 0, numPrims - 1);
                    T = BuildSBVHFast(ref Boxes, 0, numPrims - 1);
                    break;
            }

#if DEBUG
            //SaveOBJ("c:\\Temp\\LBVHInput-after-sort.obj", Vertices, Indices);
            //PrintTree("", T);
            
            // DEBUG, let's dump the BVH tree structure
            //SaveBVHToOBJ("c:\\temp\\BVH2.obj", T, Vertices, Indices);

            // Check that the refit didn't mess up the boxes
            //int temp;
            //Refit(T, out temp);
            //SaveBVHToOBJ("c:\\temp\\BVH2Refit.obj", T, Vertices, Indices, true);
#endif

#if BVH2
            int NumNodes = CountNodes(T);
            // Save the tree and the primitives
            SaveBVH(sOutFileName, NumNodes, T, Vertices, Indices, out sError);
            Console.WriteLine("Saved BVH2: " + sOutFileName);
#else
            // BVH4
            QTreeNode Q = BinTreeToQTree(T);
            int NumNodes = 0;
            Refit(Q, out NumNodes);
#if DEBUG
            SaveBVHToOBJ("c:\\temp\\BVH4.obj", Q, Vertices, Indices);
            //PrintTree("", Q);
#endif
            SaveBVH(sOutFileName, NumNodes, Q, g_EmbedVertices, Vertices, Indices, out sError);
            Console.WriteLine("Saved BVH4: " + sOutFileName);
            Q = null;
#endif

            // Tidy up
            T = null;
        }

        /// <summary>
        /// Builds one BLAS per mesh and puts them all together in the same output file.
        /// </summary>
        /// <param name="sInFileName"></param>
        /// <param name="sOutFileName"></param>
        /// <param name="sError"></param>
        public static void ComputeMultiBLAS(string sInFileName, string sOutFileName, out string sError)
        {
            var opt = OptFile.FromFile(sInFileName);
            Console.WriteLine("Loaded " + sInFileName);

            List<AABB> MeshAABBs = new List<AABB>();
            List<List<Vector>> VerticesListList = new List<List<Vector>>();
            List<List<Int32>> IndicesListList = new List<List<int>>();
            List<IGenericTree> QTrees = new List<IGenericTree>();
            AABB sceneBox = new AABB();
            sError = "";

            // Pre-pass 1:
            // * Remove all nonzero LODs
            // * Convert all faces into indexed triangles
            // * Get the scene bounds
            for (int meshIdx = 0; meshIdx < opt.Meshes.Count; meshIdx++)
            {   
                List<Vector> Vertices = new List<Vector>();
                List<Int32> Indices = new List<int>();
                List<BoxRef> Boxes = new List<BoxRef>();
                AABB meshAABB = new AABB();
                int TriId = 0;
                var mesh = opt.Meshes[meshIdx];

                // Remove all the LODs, except for the first one (we'll handle LODs later)
                for (int i = 1; i < mesh.Lods.Count; i++)
                    mesh.Lods.RemoveAt(i);

                // Copy the vertices to their final destination and update the mesh and scene
                // AABBs
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    var V = mesh.Vertices[i];
                    Vertices.Add(V);
                    meshAABB.Expand(V);
                    sceneBox.Expand(V);
                }
                MeshAABBs.Add(meshAABB);

                //foreach (var Lod in mesh.Lods)
                // Let's do a BVH for the first LOD for now...
                var Lod = mesh.Lods[0];
                {
                    // Populate the Indices list and build the boxrefs
                    foreach (var faceGroup in Lod.FaceGroups)
                    {
                        foreach (var face in faceGroup.Faces)
                        {
                            BoxRef boxref;
                            int NumIndices;

                            // Add the indices for the current triangle
                            Indices.Add(face.VerticesIndex.A);
                            Indices.Add(face.VerticesIndex.B);
                            Indices.Add(face.VerticesIndex.C);
                            NumIndices = Indices.Count;

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
                                Indices.Add(face.VerticesIndex.A);
                                Indices.Add(face.VerticesIndex.C);
                                Indices.Add(face.VerticesIndex.D);
                                NumIndices = Indices.Count;

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

#if DEBUG
                // Checkpoint: save an OBJ file and check that everything looks fine
                SaveOBJ("c:\\Temp\\LBVHInput.obj", Vertices, Indices);
#endif

                // Pre-pass 2:
                // * Compute the Morton codes.
                foreach (BoxRef boxref in Boxes)
                {
                    XwVector centroid = boxref.box.GetCentroid();
                    Vector C = new Vector(centroid.x, centroid.y, centroid.z);
                    boxref.centroid = C;

                    // Get the Morton code
                    Normalize(ref C, meshAABB);
                    boxref.code = GetMortonCode32(C);
                }

                // Sort the primitives by their Morton codes
                Boxes.Sort();

                int numPrims = Boxes.Count;
                Console.WriteLine("numPrims: " + numPrims);

                // Build the tree proper
                TreeNode T = BuildSBVHFast(ref Boxes, 0, numPrims - 1);

                // DEBUG, let's dump the BVH tree structure
#if DEBUG
                //SaveBVHToOBJ("c:\\temp\\BVH2.obj", T, Vertices, Indices);
#endif

                // Convert to BVH4 and save it to the list of QTrees
                QTreeNode Q = BinTreeToQTree(T);
                int NumNodes = 0;
                Refit(Q, out NumNodes);
                Q.SetNumNodes(NumNodes);
                QTrees.Add(Q);
                VerticesListList.Add(Vertices);
                IndicesListList.Add(Indices);
#if DEBUG
                //SaveBVHToOBJ("c:\\temp\\BVH4-" + meshIdx + ".obj", Q, Vertices, Indices);
                //PrintTree("", Q);
#endif
                //SaveBVH(sOutFileName, NumNodes, Q, g_EmbedVertices, Vertices, Indices, out sError);
                //Console.WriteLine("Saved BVH4: " + sOutFileName);
                //Q = null;
                
                // Tidy up
                T = null;
            }
            Console.WriteLine("Scene bounds: " + sceneBox.ToString());

            // Save the list of QTrees to a file
            SaveMultiBLAS(sOutFileName, QTrees, VerticesListList, IndicesListList, out sError);

            // Tidy up
            QTrees.Clear();
        }

        private static void SaveBVH(string sOutFileName, int NumNodes, IGenericTree T,
            bool EmbedVertices, List<Vector> Vertices, List<int> Indices,
            out string sError)
        {
            System.IO.BinaryWriter file = new BinaryWriter(File.OpenWrite(sOutFileName));
            sError = "";

            // Save the Magic Word/Version
            {
                string Magic = "BVH" + T.GetArity() + "-1.0";
                byte[] data = Encoding.ASCII.GetBytes(Magic);
                file.Write(data);
            }

            if (!EmbedVertices)
            {
                // Save the vertices
                {
                    // Write the number of vertices
                    UInt32 NumVertices = (UInt32)Vertices.Count;
                    file.Write(NumVertices);
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
            }

            // Save the BVH
            {
                EncodeNodes(file, NumNodes, T, EmbedVertices, Vertices, Indices);
            }

            // We don't need to save the AABBs nor Vertex Counts anymore
#if DISABLED
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
#endif

            file.Close();
        }

        private static void SaveMultiBLAS(string sOutFileName, List<IGenericTree> Trees,
            List<List<Vector>> VerticesListList, List<List<Int32>> IndicesListList,
            out string sError)
        {
            System.IO.BinaryWriter file = new BinaryWriter(File.OpenWrite(sOutFileName));
            sError = "";

            // Save the Magic Word/Version
            {
                string Magic = "BVH4-1.1";
                byte[] data = Encoding.ASCII.GetBytes(Magic);
                file.Write(data);
            }

            // Save the number of trees in the list
            {
                UInt32 NumTrees = (UInt32 )Trees.Count;
                file.Write(NumTrees);
            }

            // Save the BLASes
            {
                for (int i = 0; i < Trees.Count; i++)
                {
                    EncodeNodes(file, Trees[i].GetNumNodes(), Trees[i], true,
                        VerticesListList[i], IndicesListList[i]);
                }
            }

            file.Close();
        }

        /// <summary>
        /// Encodes the nodes in a BVH.
        /// </summary>
        private static void EncodeNodes(BinaryWriter file, int NumNodes, IGenericTree root, bool EmbedVertices=false,
            List<Vector> Vertices=null, List<Int32> Indices=null)
        {
            if (root == null)
                return;

            int arity = root.GetArity();
            // Write the number of nodes in the tree
            file.Write(NumNodes);
            Console.WriteLine("Encoding " + NumNodes + " BVH nodes to disk");

            // A breadth-first traversal will ensure that each level of the tree is written to disk
            // before advancing to the next level. We can thus keep track of the offset in the file
            // where the next node will appear.
            Queue<IGenericTree> Q = new Queue<IGenericTree>();

            // Initialize the queue and the offsets. Note that the offsets are relative to the
            // on-disk beginning of the BVH -- not absolute offsets from the beginning of the file.
            Q.Enqueue(root);
            // Since we're going to put this data in an array, it's easier to specify the children
            // offsets as indices into this array.
            int nextNode = 1;

            while (Q.Count != 0)
            {
                IGenericTree T = Q.Dequeue();
                List<IGenericTree> children = T.GetChildren();

                // In a breadth-first search, the left child will always be at offset nextNode
                switch (arity)
                {
                    case 2:
                        file.Write(
                            BVHEncoder.EncodeTreeNode2((TreeNode)T, -1 /* parent (TODO) */,
                                children.Count > 0 ? nextNode : -1,
                                children.Count > 1 ? nextNode + 1 : -1)
                        );
                        break;
                    case 4:
                        // Encode BVH4 nodes
                        // EncodeTreeNode4 needs to be made to work for arity N in order to make this
                        // case generic.
                        List<Int32> childOfs = new List<Int32>();
                        for (int i = 0; i < arity; i++)
                            childOfs.Add(i < children.Count ? nextNode + i : -1);

                        file.Write(
                            BVHEncoder.EncodeTreeNode4(T, -1 /* parent (TODO) */, childOfs,
                                EmbedVertices, Vertices, Indices)
                        );

                        // Enqueue the children
                        foreach (var child in children)
                        {
                            Q.Enqueue(child);
                            nextNode++;
                        }
                        break;
                }
            }
        }

    }
}
