
public class ComObjProps
{
   private ComObjProps(Stream stream)
   {
      height = stream.GetByte();
      massStuff = stream.GetUShort();
      flags = stream.GetByte();
      // the only thing worth more than 255 is the Lotus Esprit!
      monetaryValue = (byte) stream.GetUShort();
      qualityClass = stream.GetByte();
      otherFlags = stream.GetByte();
      scale = stream.GetByte();
      unknown = stream.GetByte();
      qualityType = stream.GetByte();
   }

   public static ComObjProps[] GetAllProps(string filename)
   {
      Stream stream = new Stream(filename);
      stream.Seek(2); // skip wee header
      int numProps = (stream.GetLength() - 2) / 11;
      ComObjProps[] props = new ComObjProps[numProps];
      for (int i = 0; i < numProps; ++i)
      {
         props[i] = new ComObjProps(stream);
      }
      
#if false
      string s = "i height mass flags value qclass flags2 scale unk qtype type\n";
      for (int i = 0; i < numProps; ++i)
      {
          ComObjProps p = props[i];
          s += $"{i:000} {p.height:x2} {p.massStuff:x4} {p.flags:x2} {p.monetaryValue:x2} {p.qualityClass:x2}" +
               $" {p.otherFlags:x2} {p.scale:x2} {p.unknown:x2} {p.qualityType:x2} // {(EObjectType)i}\n";
      }
      System.IO.File.WriteAllText("comobjflags.txt", s);
#endif
      
      // fix some things that shouldn't be pickable uppable
      EObjectType[] unpickupables =
      {
          EObjectType.Campfire,
          EObjectType.Cauldron,
          EObjectType.PileOfDebrisA,
          EObjectType.PileOfDebrisB,
          EObjectType.PileOfDebrisC,
          EObjectType.PileOfDebrisD,
          EObjectType.PileOfDebrisE,
      };
      foreach (var unpickupable in unpickupables)
      {
          byte mask = 1 << 5;
          props[(int)unpickupable].flags &= (byte) ~mask;
      }

      return props;
   }

   public readonly byte height;
   public readonly ushort massStuff;
   private byte flags;
   public readonly byte monetaryValue;
   public readonly byte qualityClass;
   private readonly byte otherFlags;
   public readonly byte scale;
   private readonly byte unknown;
   public readonly byte qualityType;

   public bool isPortable => (flags & (1 << 5)) != 0;
   public bool canBeOwned => (otherFlags & (1 << 7)) != 0;
   public bool stackable => (flags & (1 << 6)) == 0;
   public bool floats => (unknown >> 2) == 15;
   public bool canOpenDoors => (unknown & (1 << 2)) != 0;
   public bool canBeSummoned => (unknown & (1 << 2)) == 0;
}
