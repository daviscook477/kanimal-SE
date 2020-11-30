using System;
using System.Collections.Generic;
using System.Text;

namespace kanimal
{
    public struct BoneInfo
    {
        public string name;
        public float width, height;

        public BoneInfo(string name, float width, float height)
        {
            this.name = name;
            this.width = width;
            this.height = height;
        }
    }
}
