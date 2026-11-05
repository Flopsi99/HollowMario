using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HollowMario
{
    // US ROM format metadata only. Font pixels, glyph pointers and advances are
    // read from the player's already-validated ROM, never embedded in the mod.
    internal sealed class Sm64DialogueText
    {
        internal const int LinesPerPage=6, LineHeight=16, TextWidth=128;
        // Form feeds are deliberate page breaks, not rendered glyphs. All other
        // creator wording/punctuation is unchanged; each page wraps by ROM metrics.
        internal const string Introduction=
            "Hello! The Lakitu Bros.,\n"+
            "cutting in with a live\n"+
            "update on Mario's\n"+
            "situation.\f"+
            "He has just arrived in\n"+
            "the mysterious Hallownest\n"+
            "but seems to have lost\n"+
            "his ability to Long Jump\n"+
            "or Wall Jump\n"+
            "along the way!\f"+
            "I also seem to be stuck\n"+
            "to recording from this\n"+
            "angle. What a strange\n"+
            "land, folks!\f"+
            "Can our hero prevail in\n"+
            "these dangerous caverns\n"+
            "below?\n"+
            "For now, reporting live,\n"+
            "this has been the\n"+
            "Lakitu Bros.";
        private const int SegmentOffset=0x108A40, FontPointers=0x77E8, AdvancesOffset=0xEC370;
        internal sealed class Glyph
        {
            internal int Id,Advance,InkRight;
            internal byte[] Rgba;
        }
        internal readonly Dictionary<char,Glyph> Glyphs=new Dictionary<char,Glyph>();
        internal readonly string[][] Pages;
        internal Sm64DialogueText(byte[] rom,string text)
        {
            if(rom==null||rom.Length!=8388608||Big32(rom,0)!=0x80371240u)
                throw new InvalidDataException("Dialogue needs the engine's normalized Super Mario 64 US ROM.");
            byte[] segment=Inflate(rom,SegmentOffset);
            foreach(char c in (text+" ").Distinct())
            {
                if(c!=' '&&char.IsWhiteSpace(c))continue;
                int id=Encode(c),advance=rom[AdvancesOffset+id];
                if(advance<1||advance>16)throw new InvalidDataException("Unsupported dialogue glyph advance.");
                var glyph=new Glyph{Id=id,Advance=advance,Rgba=new byte[8*16*4]};
                if(c!=' ')
                {
                    uint pointer=Big32(segment,FontPointers+id*4);
                    int offset=(int)(pointer&0xffffff);
                    if((pointer>>24)!=2||offset<0||offset>segment.Length-64)
                        throw new InvalidDataException("Unsupported dialogue font pointer.");
                    // SM64 maps a stored 16x8 IA4 tile onto an 8x16 quad with
                    // rotated/flipped texture coordinates. Output is top-down RGBA.
                    for(int y=0;y<16;y++)for(int x=0;x<8;x++)
                    {
                        int pixel=(7-x)*16+15-y;
                        int nibble=(segment[offset+pixel/2]>>(pixel%2==0?4:0))&15;
                        int at=(y*8+x)*4;
                        byte intensity=(byte)((nibble>>1)*255/7),alpha=(byte)((nibble&1)*255);
                        glyph.Rgba[at]=glyph.Rgba[at+1]=glyph.Rgba[at+2]=intensity;
                        glyph.Rgba[at+3]=alpha;
                        if(alpha>0&&intensity>0)glyph.InkRight=Math.Max(glyph.InkRight,x+1);
                    }
                }
                Glyphs.Add(c,glyph);
            }
            Pages=Paginate(text);
        }
        internal static int Encode(char c)
        {
            if(c>='0'&&c<='9')return c-'0';
            if(c>='A'&&c<='Z')return c-'A'+10;
            if(c>='a'&&c<='z')return c-'a'+36;
            switch(c)
            {
                case '\'':return 0x3E;case '.':return 0x3F;case ',':return 0x6F;
                case ' ':return 0x9E;case '!':return 0xF2;case '?':return 0xF4;
                default:throw new InvalidDataException("Unsupported SM64 dialogue character: "+c);
            }
        }
        internal int Measure(string line)
        {
            int cursor=0,right=0;
            foreach(char c in line){Glyph g=Glyphs[c];right=Math.Max(right,cursor+g.InkRight);cursor+=g.Advance;}
            return Math.Max(right,cursor);
        }
        private string[][] Paginate(string text)
        {
            var pages=new List<string[]>();
            foreach(string section in text.Split('\f'))
            {
                var lines=new List<string>();
                foreach(string hardLine in section.Replace("\r","").Split('\n'))
                {
                    string current="";
                    foreach(string word in hardLine.Split((char[])null,StringSplitOptions.RemoveEmptyEntries))
                    {
                        if(Measure(word)>TextWidth)throw new InvalidDataException("Dialogue word is wider than the textbox: "+word);
                        string candidate=current.Length==0?word:current+" "+word;
                        if(Measure(candidate)>TextWidth){lines.Add(current);current=word;}else current=candidate;
                    }
                    lines.Add(current);
                }
                for(int i=0;i<lines.Count;i+=LinesPerPage)pages.Add(lines.Skip(i).Take(LinesPerPage).ToArray());
            }
            if(pages.Count==0)throw new InvalidDataException("Empty dialogue.");
            return pages.ToArray();
        }
        private static uint Big32(byte[] bytes,int at)
        {
            if(at<0||at>bytes.Length-4)throw new InvalidDataException("Dialogue ROM data is truncated.");
            return ((uint)bytes[at]<<24)|((uint)bytes[at+1]<<16)|((uint)bytes[at+2]<<8)|bytes[at+3];
        }
        private static byte[] Inflate(byte[] rom,int start)
        {
            if(Big32(rom,start)!=0x4D494F30u)throw new InvalidDataException("Dialogue font segment is not MIO0.");
            int size=checked((int)Big32(rom,start+4));
            if(size<FontPointers+1024||size>2*1024*1024)throw new InvalidDataException("Invalid dialogue font segment size.");
            int compressed=checked(start+(int)Big32(rom,start+8)),raw=checked(start+(int)Big32(rom,start+12));
            int mask=start+16,bits=0,flags=0,output=0;var data=new byte[size];
            while(output<size)
            {
                if(bits==0){if(mask>=rom.Length)throw new InvalidDataException("Truncated MIO0 mask.");flags=rom[mask++];bits=8;}
                if((flags&128)!=0){if(raw>=rom.Length)throw new InvalidDataException("Truncated MIO0 data.");data[output++]=rom[raw++];}
                else
                {
                    if(compressed<0||compressed>rom.Length-2)throw new InvalidDataException("Truncated MIO0 reference.");
                    int token=(rom[compressed]<<8)|rom[compressed+1];compressed+=2;
                    int count=(token>>12)+3,distance=(token&4095)+1;
                    if(distance>output||count>size-output)throw new InvalidDataException("Invalid MIO0 reference.");
                    for(int i=0;i<count;i++){data[output]=data[output-distance];output++;}
                }
                flags=(flags<<1)&255;bits--;
            }
            return data;
        }
    }
}
