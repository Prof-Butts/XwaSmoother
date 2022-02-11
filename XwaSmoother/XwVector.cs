using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace XwaOpter
{
    public struct XwVector
    {
        public double x;
        public double y;
        public double z;

        public XwVector(double x, double y, double z)
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
            return string.Format(CultureInfo.InvariantCulture, "{0:R} ; {1:R} ; {2:R}", this.x, this.y, this.z);
        }

        public static XwVector Set(double z, double y, double x)
        {
            return new XwVector(x, y, z);
        }

        public static XwVector Multiply(XwVector v0, double value)
        {
            double x = v0.x * value;
            double y = v0.y * value;
            double z = v0.z * value;

            return new XwVector(x, y, z);
        }

        public static XwVector Substract(XwVector v1, XwVector v0)
        {
            double x = v1.x - v0.x;
            double y = v1.y - v0.y;
            double z = v1.z - v0.z;

            return new XwVector(x, y, z);
        }

        public static XwVector Add(XwVector v1, XwVector v0)
        {
            double x = v1.x + v0.x;
            double y = v1.y + v0.y;
            double z = v1.z + v0.z;

            return new XwVector(x, y, z);
        }

        public static XwVector Mean(XwVector v1, XwVector v0)
        {
            XwVector v;
            v = XwVector.Add(v1, v0);
            v = XwVector.Multiply(v, 0.5);
            return v;
        }

        public double Length()
        {
            return Math.Sqrt(this.x * this.x + this.y * this.y + this.z * this.z);
        }

        public static double SubstractAndLength(XwVector v1, XwVector v0)
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
            double length = v0.Length();

            if (length <= 0.0)
            {
                return new XwVector(0.0, 0.0, 0.0);
            }
            else
            {
                return XwVector.Multiply(v0, 1.0f / length);
            }
        }

        public static XwVector NormalizeAndMultiply(XwVector v0, double value)
        {
            double length = v0.Length();

            if (length <= 0.0)
            {
                return new XwVector(0.0, 0.0, 0.0);
            }
            else
            {
                return XwVector.Multiply(v0, value / length);
            }
        }

        public static XwVector CrossProduct(XwVector v0, XwVector v1)
        {
            double x = v0.y * v1.z - v0.z * v1.y;
            double y = v0.z * v1.x - v0.x * v1.z;
            double z = v0.x * v1.y - v0.y * v1.x;

            return new XwVector(x, y, z);
        }

        public static double DotProduct(XwVector v1, XwVector v0)
        {
            return v0.x * v1.x + v0.y * v1.y + v0.z * v1.z;
        }

        public static double AngleRadianToDegree(double angle)
        {
            return (angle * 180.0) / Math.PI;
        }

        public static double AcosWithAtan2(double c)
        {
            return Math.Atan2(Math.Sqrt(1.0 - c * c), c);
        }

        public static double Angle(XwVector v1, XwVector v0)
        {
            double lengthProduct = v0.Length() * v1.Length();

            double angle;

            if (lengthProduct == 0.0)
            {
                angle = 0.0;
            }
            else
            {
                double cos = XwVector.DotProduct(v1, v0) / lengthProduct;

                if (cos > 1.0)
                {
                    cos = 1.0;
                }
                else if (cos < -1.0)
                {
                    cos = -1.0;
                }

                angle = XwVector.AcosWithAtan2(cos);
            }

            return angle;
        }
    }
}
