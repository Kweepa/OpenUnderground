using UnityEngine;
using System.IO;

public class Stream
{
   byte[] bytes;
   int i;
   int buffer;
   int bufferBits;

   public Stream( string fileName )
   {
      string rootPath = GameDataPath.GetGameRootPath();
      if (rootPath == null)
      {
         // Fallback to old behavior
         bytes = File.ReadAllBytes( Path.Combine( Application.dataPath, fileName ) );
      }
      else
      {
         // Check if fileName is already an absolute path
         if (Path.IsPathRooted(fileName))
         {
            // Already absolute, use as-is
            bytes = File.ReadAllBytes(fileName);
         }
         else
         {
            // Handle relative paths like "../Data/file.dat" or "Data/file.dat"
            string normalizedPath = fileName;
            if (normalizedPath.StartsWith("../"))
            {
               // Remove "../" prefix
               normalizedPath = normalizedPath.Substring(3);
            }
            bytes = File.ReadAllBytes( Path.Combine( rootPath, normalizedPath ) );
         }
      }
   }

   public int GetLength()
   {
      return bytes.Length;
   }

   public byte GetByte()
   {
      return bytes[i++];
   }

   public ushort GetUShort()
   {
      ushort val = (ushort) ( bytes[i] + 256 * bytes[i + 1] );
      i += 2;
      return val;
   }

   public short GetShort()
   {
       ushort val = GetUShort();
       return (short)val;
   }

   private uint GetUInt()
   {
      uint val = ( bytes[i] + 256u * ( bytes[i + 1] + 256u * ( bytes[i + 2] + 256u * bytes[i + 3] ) ) );
      i += 4;
      return val;
   }

   public int GetInt()
   {
       // NOTE: just a convenience, doesn't actually read a negative value
       return (int)GetUInt();
   }

   public int[] GetIntArray( int count )
   {
       int[] arr = new int[count];
       for ( int c = 0; c < count; ++c )
       {
           arr[c] = GetInt();
       }
       return arr;
   }

   public uint[] GetUIntArray( int count )
   {
      uint[] arr = new uint[count];
      for ( int c = 0; c < count; ++c )
      {
         arr[c] = GetUInt();
      }
      return arr;
   }

   public ushort[] GetUShortArray( int count )
   {
      ushort[] arr = new ushort[count];
      for ( int c = 0; c < count; ++c )
      {
         arr[c] = GetUShort();
      }
      return arr;
   }

   public short[] GetShortArray(int count)
   {
       short[] arr = new short[count];
       for (int c = 0; c < count; ++c)
       {
           arr[c] = GetShort();
       }
       return arr;
   }

   public byte[] GetByteArray( int count )
   {
      byte[] arr = new byte[count];
      for ( int c = 0; c < count; ++c )
      {
         arr[c] = GetByte();
      }
      return arr;
   }

   public byte[] GetNybbleArray( int count )
   {
      byte[] arr = new byte[count];
      byte val = 0;
      for ( int c = 0; c < count; ++c )
      {
         if ( ( c & 1 ) == 0 )
         {
            val = GetByte();
         }
         arr[c] = (byte) ( val >> 4 );
         val = (byte) ( ( val & 15 ) << 4 );
      }
      return arr;
   }

   public string GetString( int len )
   {
      string s = "";
      for ( int ii = 0; ii < len; ++ii )
      {
         s += (char) GetByte();
      }
      return s;
   }

   public void Seek( int off )
   {
      i = off;
      buffer = 0;
      bufferBits = 0;
   }

   public void Skip( int numBytes )
   {
      i += numBytes;
      buffer = 0;
      bufferBits = 0;
   }

   public bool End()
   {
      return i >= bytes.Length;
   }

   public int ReadUpperBits( int count )
   {
      bufferBits -= count;
      if ( bufferBits < 0 )
      {
         buffer = ( ( buffer << 8 ) | bytes[i++] ) & 65535;
         bufferBits += 8;
      }
      return ( buffer >> bufferBits ) & ( ( 1 << count ) - 1 );
   }

   public int ReadUpperBit()
   {
       if (--bufferBits < 0)
       {
           buffer = ( ( buffer << 8 ) | bytes[i++] ) & 65535;
           bufferBits += 8;
       }
       return (buffer >> bufferBits) & 1;
   }

   public int GetPos()
   {
      return i;
   }

   public byte[] GetRawBytes()
   {
       return bytes;
   }
}
