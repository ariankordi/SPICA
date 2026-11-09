using SPICA.Formats.Common;
using SPICA.Serialization;
using SPICA.Serialization.Attributes;

namespace SPICA.Formats.CtrGfx
{
    [Inline]
    class GfxHeader : ICustomSerialization
    {
        public uint   MagicNumber;
        public ushort ByteOrderMark;
        public ushort HeaderLength;

        [Version]
        public uint Revision;
        public int  FileLength;
        public int  SectionsCount;

        //TODO: Version 1.0.0.0 is unsupported.
        [IfVersion(CmpOp.Gequal, 0x05000000)]
        public GfxSectionHeader Data;

        public GfxHeader()
        {
            MagicNumber   = IOUtils.ToUInt32("CGFX");
            ByteOrderMark = GfxConstants.ByteOrderMark;
            HeaderLength  = GfxConstants.GfxHeaderLength;
            Revision      = GfxConstants.CGFXRevision;
            FileLength    = 0;
            SectionsCount = 0;

            Data = new GfxSectionHeader("DATA");
        }

        public void Deserialize(BinaryDeserializer Deserializer)
        {
            var Reader = Deserializer.Reader;
            long lastPos = Reader.BaseStream.Position;

            Reader.BaseStream.Seek(116, SeekOrigin.Begin);

            //M-1: HACK! Some CGFX from the E3 2010 are set to version 4.0, but are very different from the SDK version of 4.0
            if (Revision > 0x03000000 && Reader.ReadUInt32() == IOUtils.ToUInt32("DICT"))
            {
                Revision = 0x03FFFFFF;

                Deserializer.RevisionStack.Clear();
                Deserializer.RevisionStack.Push(0x03FFFFFF);
                Deserializer.MainFileVersion = 0x03FFFFFF;
            }
            else
            {
                Deserializer.MainFileVersion = Revision;
            }

            //Some revision 5.0 files (3DS Mii Maker's models and animations) have 15 dictionaries in
            //the DATA section, without the trailing Emitters one: the 16th slot already holds the
            //first "DICT" that follows the table. Skip Emitters there instead of misreading it.
            const int DictionaryEntrySize = 8;
            const int EmittersSlot = 15;
            if (Revision >= 0x05000000)
            {
                Reader.BaseStream.Seek(HeaderLength + DictionaryEntrySize + EmittersSlot * DictionaryEntrySize, SeekOrigin.Begin);
                if (Reader.ReadUInt32() == IOUtils.ToUInt32("DICT"))
                {
                    Deserializer.AbsentFields.Add("Gfx.Emitters");
                }
            }

            Reader.BaseStream.Seek(lastPos, SeekOrigin.Begin);
        }

        public bool Serialize(BinarySerializer Serializer)
        {
            return false;
        }
    }
}
