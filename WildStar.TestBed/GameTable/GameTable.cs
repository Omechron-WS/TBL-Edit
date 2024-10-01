using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using WildStar.TestBed.GameTable.IO;
using WildStar.TestBed.GameTable.Static;

namespace WildStar.TestBed.GameTable
{
    public class GameTable
    {
        public string Name { get; set; }
        public List<GameTableColumn> Columns { get; } = new List<GameTableColumn>();
        public Dictionary<uint, GameTableEntry> Entries { get; } = new Dictionary<uint, GameTableEntry>();

        public long recordSize = 0;
        public bool padEntries = false;

        /// <summary>
        /// 
        /// </summary>
        public void AddColumn(string name)
        {

        }

        /// <summary>
        /// 
        /// </summary>
        public void RemoveColumn(string name)
        {

        }

        public bool HasEntry(uint id)
        {
            return Entries.ContainsKey(id);
        }

        /// <summary>
        /// 
        /// </summary>
        public void AddEntry(GameTableEntry entry)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public void AddEntry(GameTableEntry entry, uint id)
        {
            if (HasEntry(id))
                return;

            entry.AddInteger(id, 0);
            Entries.Add(id, entry);
        }

        /// <summary>
        /// 
        /// </summary>
        public void RemoveEntry(uint id)
        {
            Entries.Remove(id);
        }





        /// <summary>
        /// 
        /// </summary>
        public void Save(string path)
        {
            using (FileStream stream = File.OpenWrite(path))
            using (var writer = new BinaryWriter(stream))
            {
                var headerSize = Marshal.SizeOf<Header>();


                var header = new Header
                {
                    Signature = 0x4454424C
                };

                //
                writer.Write(new byte[Marshal.SizeOf<Header>()]);

                // name
                header.NameLength = Name.Length + 1;
                writer.WriteWideStringPad(Name, 16);

                // columns
                header.ColumnCount  = Columns.Count;
                header.ColumnOffset = stream.Position - headerSize;
                WriteColumns(writer);

                // entries
                header.RecordCount = Entries.Count;
                header.RecordOffset = stream.Position - headerSize;

                if (header.RecordCount > 0)
                {
                    header.RecordSize = Entries.Values.First().CalculateSize(padEntries);
                    if(header.RecordSize != recordSize)
                    {
                        padEntries = true;
                        header.RecordSize = Entries.Values.First().CalculateSize(padEntries);
                    }
                }

                WriteEntries(writer, stream, ref header);

                // write header
                var span = MemoryMarshal.CreateReadOnlySpan(ref header, 1);
                var bytes = MemoryMarshal.Cast<Header, byte>(span).ToArray();
                stream.Position = 0;
                writer.Write(bytes);
            }
        }

        private void PadTo16(BinaryWriter writer)
        {
            long padding = 16 - writer.BaseStream.Position % 16L;
            if (padding == 16)
                padding = 0;

            for (int i = 0; i < padding; ++i)
                writer.Write((byte)0);
        }

        private void WriteColumns(BinaryWriter writer)
        {
            using (var stringTableStream = new MemoryStream())
            using (var stringTableWriter = new BinaryWriter(stringTableStream))
            {
                var columns = new TblColumn[Columns.Count];
                for (var i = 0; i < Columns.Count; i++)
                {
                    columns[i].Type = Columns[i].Type;
                    columns[i].Unknown2 = Columns[i].Unknown2;
                    columns[i].Unknown3 = Columns[i].Unknown3;


                    columns[i].NameOffset = stringTableStream.Position;
                    stringTableWriter.WriteWideStringPad(Columns[i].Name, 16);
                    columns[i].NameLength = Columns[i].Name.Length + 1;


                    columns[i].Write(writer);
                }

                // why??
                if (writer.BaseStream.Position % 16 != 0)
                    writer.Write(new byte[writer.BaseStream.Position % 16]);

                writer.Write(stringTableStream.ToArray());
            }
        }

        private void WriteEntries(BinaryWriter writer, FileStream stream, ref Header header)
        {
            header.MaxId = Entries.Keys.Max();
            var lookup = new int[header.MaxId + 1];
            for (int i = 0; i < header.MaxId; i++)
                lookup[i] = -1;

            uint entrySize = Entries.Values.First().CalculateSize(padEntries);
            var stringTableOffset = entrySize * Entries.Count;

            Dictionary<string, uint> stringLookup = new Dictionary<string, uint>();

            int entryCounter = 0;

            using (var stringTableStream = new MemoryStream())
            using (var stringTableWriter = new BinaryWriter(stringTableStream))
            {
                foreach (GameTableEntry entry in Entries.Values)
                {
                    uint id = entry.Values[0].GetValue<uint>();
                    lookup[id] = entryCounter;
                    entryCounter += 1;

                    long start = stream.Position;
                    foreach (GameTableValue value in entry.Values)
                    {
                        switch (value.Type)
                        {
                            case DataType.Integer:
                                writer.Write((uint)value.Value);
                                break;
                            case DataType.Single:
                                writer.Write((float)value.Value);
                                break;
                            case DataType.Boolean:
                                writer.Write(Convert.ToUInt32((bool)value.Value));
                                break;
                            case DataType.Long:
                                writer.Write((ulong)value.Value);
                                break;
                            case DataType.String:
                                long a = (stream.Position - start) % 8;
                                if (a != 0)
                                {
                                    writer.Write((uint)0);
                                }
                                uint stringPosition = 0;
                                if (stringLookup.TryGetValue((string)value.Value, out uint position))
                                {
                                    stringPosition = position;
                                }
                                else
                                {
                                    stringPosition = (uint)stringTableOffset + (uint)stringTableStream.Position;
                                    stringLookup.Add((string)value.Value, stringPosition);

                                    stringTableWriter.WriteWideString((string)value.Value);
                                }
                                writer.Write(stringPosition);
                                writer.Write((uint)0);
                                break;
                        }
                    }

                    long end = stream.Position - start;
                    if (end != entrySize)
                    {
                        long difference = entrySize - end;
                        writer.Write(new byte[difference]);
                    }
                }




                writer.Write(stringTableStream.ToArray());
            }

            var headerSize = Marshal.SizeOf<Header>();

            header.TotalRecordSize = stream.Position - headerSize - header.RecordOffset;

            PadTo16(writer);

            // build lookup table
            header.MaxId = Entries.Keys.Max() + 1;
            header.LookupOffset = stream.Position - headerSize;

            for (int i = 0; i < header.MaxId; i++)
                writer.Write(lookup[i]);

            PadTo16(writer);
        }



        /// <summary>
        /// 
        /// </summary>
        public void Load(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, Encoding.Unicode))
            {
                var headerSize = Marshal.SizeOf<Header>();
                Header header = MemoryMarshal.Read<Header>(reader.ReadBytes(headerSize));

                recordSize = header.RecordSize;


                // name
                var nameLength = reader.ReadBytes(((int)header.NameLength - 1) * 2);
                Name = Encoding.Unicode.GetString(nameLength);




                // columns
                long columnDataOffset = headerSize + header.ColumnOffset;
                stream.Position = columnDataOffset;
                ReadOnlySpan<TblColumn> columns = MemoryMarshal.Cast<byte, TblColumn>(
                    reader.ReadBytes(Marshal.SizeOf<TblColumn>() * (int)header.ColumnCount));

                // column names
                long columnStringTableOffset = columnDataOffset + (Marshal.SizeOf<TblColumn>() * header.ColumnCount);

                // why??
                if (columnStringTableOffset % 16 != 0)
                    columnStringTableOffset += columnStringTableOffset % 16;


                foreach (TblColumn column in columns)
                {
                    long columnNamePosition = columnStringTableOffset + column.NameOffset;
                    stream.Position = columnNamePosition;


                    var columnNameBytes = reader.ReadBytes(((int)column.NameLength - 1) * 2);
                    string columnName = Encoding.Unicode.GetString(columnNameBytes);

                    // bla
                    Columns.Add(new GameTableColumn(columnName, column));
                }



                // records
                long recordDataOffset = headerSize + header.RecordOffset;
                for (var i = 0; i < header.RecordCount; i++)
                {
                    stream.Position = recordDataOffset + header.RecordSize * i;



                    var values = new List<GameTableValue>(columns.Length);
                    foreach (GameTableColumn column in Columns)
                    {
                        var value = new GameTableValue(column.Type);

                        switch (column.Type)
                        {
                            case DataType.Integer:
                                value.SetValue(reader.ReadUInt32());
                                break;
                            case DataType.Single:
                                value.SetValue(reader.ReadSingle());
                                break;
                            case DataType.Boolean:
                                value.SetValue(Convert.ToBoolean(reader.ReadUInt32()));
                                break;
                            case DataType.Long:
                                value.SetValue(reader.ReadUInt64());
                                break;
                            case DataType.String:
                            {
                                uint offset1 = reader.ReadUInt32();
                                uint offset2 = reader.ReadUInt32();
                                if (offset1 == 0)
                                    reader.ReadUInt32();

                                uint offset3 = Math.Max(offset1, offset2);

                                // read string
                                long position = stream.Position;
                                stream.Position =
                                    headerSize + header.RecordOffset + offset3;


                                value.SetValue(reader.ReadWideString());

                                stream.Position = position;


                                break;
                            }
                        }

                        values.Add(value);
                    }



                    var lol = new GameTableEntry(values);
                    Entries.Add(values[0].GetValue<uint>(), lol);

                    
                }

                // ignore lookup table

            }
        }
    }
}
