using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Globalization;
using NLog;
using System.IO;

namespace kanimal
{
    public class DebonerProcessor : Processor
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public override XmlDocument Process(XmlDocument original)
        {
            Logger.Info("Deboning.");
            /* clone to avoid modifying original data */
            XmlDocument processedScml = (XmlDocument)original.Clone();

            XmlElement spriterData = (XmlElement)processedScml.GetElementsByTagName("spriter_data")[0];
            XmlElement entity = GetFirstChildByName(spriterData, "entity");
            Dictionary<string, BoneInfo> boneInfo = buildBoneInfo(entity);
            foreach (XmlNode node0 in entity.ChildNodes)
            {
                if (node0 is XmlElement && node0.Name.Equals("animation"))
                {
                    XmlElement animation = (XmlElement)node0;
                    Dictionary<int, TimelineInfo> timelineInfo = buildTimelineInfo(animation);
                    XmlElement mainline = GetFirstChildByName(animation, "mainline");
                    foreach (XmlNode node1 in mainline.ChildNodes)
                    {
                        if (node1 is XmlElement && node1.Name.Equals("key"))
                        {
                            XmlElement key = (XmlElement)node1;
                            (int unparentedBone, int unparentedBoneTimeline, int unparentedBoneKey) = findUnparentedBone(key);
                            while (unparentedBone != -1)
                            {
                                // find all objs in the frame that are parented to the bone w/o parents
                                // and then make these objs unparented too
                                List<(int, int)> objs = findParentedBy(key, unparentedBone);
                                deparent(key, objs);

                                // modify the transforms of the objs using their previous parent's transform
                                foreach ((int timeline_id, int key_id) in objs)
                                {
                                    float px = timelineInfo[unparentedBoneTimeline].frames[unparentedBoneKey].x;
                                    float py = timelineInfo[unparentedBoneTimeline].frames[unparentedBoneKey].y;
                                    float ptheta = timelineInfo[unparentedBoneTimeline].frames[unparentedBoneKey].angle;
                                    float pscale_x = timelineInfo[unparentedBoneTimeline].frames[unparentedBoneKey].scale_x;
                                    float pscale_y = timelineInfo[unparentedBoneTimeline].frames[unparentedBoneKey].scale_y;

                                    float cx = timelineInfo[timeline_id].frames[key_id].x;
                                    float cy = timelineInfo[timeline_id].frames[key_id].y;
                                    float ctheta = timelineInfo[timeline_id].frames[key_id].angle;
                                    float cscale_x = timelineInfo[timeline_id].frames[key_id].scale_x;
                                    float cscale_y = timelineInfo[timeline_id].frames[key_id].scale_y;

                                    float mx = px + MathF.Cos(MathF.PI / 180f * ptheta) * pscale_x * cx - MathF.Sin(MathF.PI / 180f * ptheta) * pscale_y * cy;
                                    float my = py + MathF.Sin(MathF.PI / 180f * ptheta) * pscale_x * cx + MathF.Cos(MathF.PI / 180f * ptheta) * pscale_y * cy;
                                    float mtheta = ptheta + ctheta;
                                    float mscale_x = pscale_x * cscale_x;
                                    float mscale_y = pscale_y * cscale_y;

                                    updateKey(animation, timeline_id, key_id, mx, my, mtheta, mscale_x, mscale_y);
                                    timelineInfo[timeline_id].frames[key_id].x = mx;
                                    timelineInfo[timeline_id].frames[key_id].y = my;
                                    timelineInfo[timeline_id].frames[key_id].angle = mtheta;
                                    timelineInfo[timeline_id].frames[key_id].scale_x = mscale_x;
                                    timelineInfo[timeline_id].frames[key_id].scale_y = mscale_y;
                                }

                                removeBone(key, unparentedBone);
                                (unparentedBone, unparentedBoneTimeline, unparentedBoneKey) = findUnparentedBone(key);
                            }
                        }
                    }
                }
            }

            return processedScml;
        }

        private void updateKey(XmlElement animation, int timeline_id, int key_id, float x, float y, float theta, float scale_x, float scale_y)
        {
            foreach (XmlNode node0 in animation.ChildNodes)
            {
                if (node0 is XmlElement && node0.Name.Equals("timeline"))
                {
                    XmlElement timeline = (XmlElement)node0;
                    if (int.Parse(timeline.GetAttribute("id"), CultureInfo.InvariantCulture) == timeline_id)
                    {
                        foreach (XmlNode node1 in timeline.ChildNodes)
                        {
                            if (node1 is XmlElement && node1.Name.Equals("key"))
                            {
                                XmlElement key = (XmlElement)node1;
                                if (int.Parse(key.GetAttribute("id"), CultureInfo.InvariantCulture) == key_id)
                                {
                                    XmlElement obj = GetFirstChildByName(key, "object");
                                    if (obj == null)
                                    {
                                        obj = GetFirstChildByName(key, "bone");
                                    }
                                    obj.SetAttribute("x", x.ToString());
                                    obj.SetAttribute("y", y.ToString());
                                    obj.SetAttribute("angle", theta.ToString());
                                    obj.SetAttribute("scale_x", scale_x.ToString());
                                    obj.SetAttribute("scale_y", scale_y.ToString());
                                    return;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void removeBone(XmlElement key, int bone)
        {
            XmlNode toRemove = null;
            foreach (XmlNode node in key.ChildNodes)
            {
                if (node is XmlElement && node.Name.Equals("bone_ref"))
                {
                    XmlElement bone_ref = (XmlElement)node;
                    int bone_id = int.Parse(bone_ref.GetAttribute("id"), CultureInfo.InvariantCulture);
                    if (bone_id == bone)
                    {
                        toRemove = bone_ref;
                        break;
                    }
                }
            }
            if (toRemove != null)
            {
                key.RemoveChild(toRemove);
            }
        }

        private void deparent(XmlElement key, List<(int, int)> objs)
        {
            HashSet<int> ints = new HashSet<int>();
            foreach ((int timeline_id, int key_id) in objs)
            {
                ints.Add(timeline_id);
            }
            foreach (XmlNode node in key.ChildNodes)
            {
                if (node is XmlElement && (node.Name.Equals("bone_ref") || node.Name.Equals("object_ref")))
                {
                    XmlElement obj_ref = (XmlElement)node;
                    if (!obj_ref.HasAttribute("parent"))
                    {
                        continue;
                    }
                    int timeline_id = int.Parse(obj_ref.GetAttribute("timeline"), CultureInfo.InvariantCulture);
                    if (ints.Contains(timeline_id))
                    {
                        obj_ref.RemoveAttribute("parent");
                    }
                }
            }
        }

        private List<(int, int)> findParentedBy(XmlElement key, int parent)
        {
            List<(int, int)> objs = new List<(int, int)>();
            foreach (XmlNode node in key.ChildNodes)
            {
                if (node is XmlElement && (node.Name.Equals("bone_ref") || node.Name.Equals("object_ref")))
                {
                    XmlElement obj_ref = (XmlElement)node;
                    if (!obj_ref.HasAttribute("parent"))
                    {
                        continue;
                    }
                    int parent_id = int.Parse(obj_ref.GetAttribute("parent"), CultureInfo.InvariantCulture);
                    if (parent_id == parent)
                    {
                        int timeline_id = int.Parse(obj_ref.GetAttribute("timeline"), CultureInfo.InvariantCulture);
                        int key_id = int.Parse(obj_ref.GetAttribute("key"), CultureInfo.InvariantCulture);
                        objs.Add((timeline_id, key_id));
                    }
                }
            }
            return objs;
        }

        private (int, int, int) findUnparentedBone(XmlElement key)
        {
            foreach (XmlNode node in key.ChildNodes)
            {
                if (node is XmlElement && node.Name.Equals("bone_ref"))
                {
                    XmlElement bone_ref = (XmlElement)node;
                    if (!bone_ref.HasAttribute("parent"))
                    {
                        int bone_id = int.Parse(bone_ref.GetAttribute("id"), CultureInfo.InvariantCulture);
                        int timeline_id = int.Parse(bone_ref.GetAttribute("timeline"), CultureInfo.InvariantCulture);
                        int key_id = int.Parse(bone_ref.GetAttribute("key"), CultureInfo.InvariantCulture);
                        return (bone_id, timeline_id, key_id);
                    }
                }
            }
            return (-1, -1, -1);
        }

        private Dictionary<int, TimelineInfo> buildTimelineInfo(XmlElement animation)
        {
            Dictionary<int, TimelineInfo> timelineInfo = new Dictionary<int, TimelineInfo>();
            foreach (XmlNode node in animation.ChildNodes)
            {
                if (node is XmlElement && node.Name.Equals("timeline"))
                {
                    XmlElement timeline = (XmlElement)node;
                    int id = int.Parse(timeline.GetAttribute("id"), CultureInfo.InvariantCulture);
                    string name = timeline.GetAttribute("name");
                    bool bone = timeline.HasAttribute("object_type") && timeline.GetAttribute("object_type") == "bone";
                    timelineInfo.Add(id, new TimelineInfo(id, name, bone, buildFrameInfo(timeline)));
                }
            }
            return timelineInfo;
        }

        private Dictionary<int, FrameInfo> buildFrameInfo(XmlElement timeline)
        {
            Dictionary<int, FrameInfo> frameInfo = new Dictionary<int, FrameInfo>();
            foreach (XmlNode node in timeline.ChildNodes)
            {
                if (node is XmlElement && node.Name.Equals("key"))
                {
                    XmlElement key = (XmlElement)node;
                    int id = int.Parse(key.GetAttribute("id"), CultureInfo.InvariantCulture);
                    XmlElement obj = GetFirstChildByName(key, "object");
                    int folder = -1;
                    int file = -1;
                    if (obj == null)
                    {
                        obj = GetFirstChildByName(key, "bone");
                    }
                    else
                    {
                        folder = int.Parse(obj.GetAttribute("folder"), CultureInfo.InvariantCulture);
                        file = int.Parse(obj.GetAttribute("file"), CultureInfo.InvariantCulture);
                    }
                    float x = float.Parse(obj.GetAttribute("x"), CultureInfo.InvariantCulture);
                    float y = float.Parse(obj.GetAttribute("y"), CultureInfo.InvariantCulture);
                    float angle = float.Parse(obj.GetAttribute("angle"), CultureInfo.InvariantCulture);
                    float scale_x = float.Parse(obj.GetAttribute("scale_x"), CultureInfo.InvariantCulture);
                    float scale_y = float.Parse(obj.GetAttribute("scale_y"), CultureInfo.InvariantCulture);
                    frameInfo.Add(id, new FrameInfo(folder, file, x, y, angle, scale_x, scale_y));
                }
            }
            return frameInfo;
        }

        private Dictionary<string, BoneInfo> buildBoneInfo(XmlElement entity)
        {
            Dictionary<string, BoneInfo> boneInfo = new Dictionary<string, BoneInfo>();
            foreach (XmlNode node in entity.ChildNodes)
            {
                if (node is XmlElement && node.Name.Equals("obj_info"))
                {
                    XmlElement obj_info = (XmlElement)node;
                    if (obj_info.GetAttribute("type") == "bone")
                    {
                        boneInfo.Add(obj_info.GetAttribute("name"),
                            new BoneInfo(obj_info.GetAttribute("name"),
                            float.Parse(obj_info.GetAttribute("w"), CultureInfo.InvariantCulture),
                            float.Parse(obj_info.GetAttribute("h"), CultureInfo.InvariantCulture)));
                    }
                }
            }
            return boneInfo;
        }

        private XmlElement GetFirstChildByName(XmlElement parent, string tagName)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                if (node is XmlElement && node.Name.Equals(tagName))
                {
                    return (XmlElement)node;
                }
            }
            return null;
        }
    }
}
