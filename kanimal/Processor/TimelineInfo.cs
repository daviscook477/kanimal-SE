using System;
using System.Collections.Generic;
using System.Text;

namespace kanimal
{
    public struct TimelineInfo
    {
        public int id;
        public string name;
        public bool bone;
        public Dictionary<int, FrameInfo> frames;

        public TimelineInfo(int id, string name, bool bone, Dictionary<int, FrameInfo> frames)
        {
            this.id = id;
            this.name = name;
            this.bone = bone;
            this.frames = frames;
        }
    }
}
