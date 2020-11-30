using System;
using System.Collections.Generic;
using System.Text;

namespace kanimal
{
    public class FrameInfo
    {
        public int folder, file;
        public float x, y, angle, scale_x, scale_y;

        public FrameInfo(int folder, int file, float x, float y, float angle, float scale_x, float scale_y)
        {
            this.folder = folder;
            this.file = file;
            this.x = x;
            this.y = y;
            this.angle = angle;
            this.scale_x = scale_x;
            this.scale_y = scale_y;
        }
    }
}
