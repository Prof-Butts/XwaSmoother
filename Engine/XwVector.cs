using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace XwaOpter
{
    public struct XwVector
    {
        public float x;
        public float y;
        public float z;

        public XwVector(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public XwVector(JeremyAnsel.Xwa.Opt.Vector v)
        {
            this.x = v.X;
            this.y = v.Y;
            this.z = v.Z;
        }

        public JeremyAnsel.Xwa.Opt.Vector ToOptVector()
        {
            return new JeremyAnsel.Xwa.Opt.Vector((float)this.x, (float)this.y, (float)this.z);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:R}, {1:R}, {2:R}", this.x, this.y, this.z);
        }

        public void SetMaxFloat()
        {
            this.x = float.MaxValue;
            this.y = float.MaxValue;
            this.z = float.MaxValue;
        }

        public void SetMinFloat()
        {
            this.x = float.MinValue;
            this.y = float.MinValue;
            this.z = float.MinValue;
        }

        public float GetComponent(int key)
        {
            switch (key) {
                case 0: return this.x;
                case 1: return this.y;
                case 2: return this.z;
            }
            return float.NaN;
        }

        public void SetComponent(int key, float val)
        {
            switch (key) {
                case 0: this.x = val; break;
                case 1: this.y = val; break;
                case 2: this.z = val; break;
            }
        }

        public float this[int key]
        {
            get => GetComponent(key);
            set => SetComponent(key, value);
        }

        public static XwVector Set(float z, float y, float x)
        {
            return new XwVector(x, y, z);
        }

        public static XwVector Multiply(XwVector v0, float value)
        {
            float x = v0.x * value;
            float y = v0.y * value;
            float z = v0.z * value;

            return new XwVector(x, y, z);
        }

        public static XwVector Substract(XwVector v1, XwVector v0)
        {
            float x = v1.x - v0.x;
            float y = v1.y - v0.y;
            float z = v1.z - v0.z;

            return new XwVector(x, y, z);
        }

        public static XwVector Add(XwVector v1, XwVector v0)
        {
            float x = v1.x + v0.x;
            float y = v1.y + v0.y;
            float z = v1.z + v0.z;

            return new XwVector(x, y, z);
        }

        public static XwVector Mean(XwVector v1, XwVector v0)
        {
            XwVector v;
            v = XwVector.Add(v1, v0);
            v = XwVector.Multiply(v, 0.5f);
            return v;
        }

        public float Length()
        {
            return (float)Math.Sqrt(this.x * this.x + this.y * this.y + this.z * this.z);
        }

        public void Multiply(float val)
        {
            this.x *= val;
            this.y *= val;
            this.z *= val;
        }

        public static float SubstractAndLength(XwVector v1, XwVector v0)
        {
            XwVector v;
            v = XwVector.Substract(v1, v0);
            return v.Length();
        }

        public static bool NearEqual(XwVector v1, XwVector v0)
        {
            return XwVector.SubstractAndLength(v1, v0) < 0.0001;
        }

        public static XwVector Normalize(XwVector v0)
        {
            float length = v0.Length();

            if (length <= 0.0)
            {
                return new XwVector(0.0f, 0.0f, 0.0f);
            }
            else
            {
                return XwVector.Multiply(v0, 1.0f / length);
            }
        }

        public static XwVector NormalizeAndMultiply(XwVector v0, float value)
        {
            float length = v0.Length();

            if (length <= 0.0)
            {
                return new XwVector(0.0f, 0.0f, 0.0f);
            }
            else
            {
                return XwVector.Multiply(v0, value / length);
            }
        }

        public static XwVector CrossProduct(XwVector v0, XwVector v1)
        {
            float x = v0.y * v1.z - v0.z * v1.y;
            float y = v0.z * v1.x - v0.x * v1.z;
            float z = v0.x * v1.y - v0.y * v1.x;

            return new XwVector(x, y, z);
        }

        public static float DotProduct(XwVector v1, XwVector v0)
        {
            return v0.x * v1.x + v0.y * v1.y + v0.z * v1.z;
        }

        public static float AngleRadianToDegree(float angle)
        {
            return (angle * 180.0f) / (float)Math.PI;
        }

        public static float AcosWithAtan2(float c)
        {
            return (float)Math.Atan2(Math.Sqrt(1.0 - c * c), c);
        }

        public static float Angle(XwVector v1, XwVector v0)
        {
            float lengthProduct = v0.Length() * v1.Length();

            float angle;

            if (lengthProduct == 0.0)
            {
                angle = 0.0f;
            }
            else
            {
                float cos = XwVector.DotProduct(v1, v0) / lengthProduct;

                if (cos > 1.0)
                {
                    cos = 1.0f;
                }
                else if (cos < -1.0)
                {
                    cos = -1.0f;
                }

                angle = XwVector.AcosWithAtan2(cos);
            }

            return angle;
        }
    }
}
