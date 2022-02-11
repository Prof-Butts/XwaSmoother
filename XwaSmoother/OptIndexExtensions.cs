using JeremyAnsel.Xwa.Opt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace XwaOpter
{
    static class OptIndexExtensions
    {
        public static int AtIndex(this Index index, int i)
        {
            switch (i)
            {
                case 0:
                    return index.A;

                case 1:
                    return index.B;

                case 2:
                    return index.C;

                case 3:
                    return index.D;

                default:
                    throw new ArgumentOutOfRangeException("i");
            }
        }

        public static Index SetAtIndex(this Index index, int i, int value)
        {
            switch (i)
            {
                case 0:
                    index.A = value;
                    break;

                case 1:
                    index.B = value;
                    break;

                case 2:
                    index.C = value;
                    break;

                case 3:
                    index.D = value;
                    break;

                default:
                    throw new ArgumentOutOfRangeException("i");
            }

            return index;
        }
    }
}
