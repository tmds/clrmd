﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Runtime.Linux
{
    internal class ElfFile
    {
        private readonly Reader _reader;
        private readonly long _position;
        private readonly bool _virtual;

        private Reader _virtualAddressReader;
        private ElfNote[] _notes;
        private ElfProgramHeader[] _programHeaders;
        private ElfSectionHeader[] _sections;
        private string[] _sectionNames;

        public ElfHeader Header { get; }

        public IReadOnlyCollection<ElfNote> Notes
        {
            get
            {
                LoadNotes();
                return _notes;
            }
        }
        public IReadOnlyList<ElfProgramHeader> ProgramHeaders
        {
            get
            {
                LoadProgramHeaders();
                return _programHeaders;
            }
        }

        public Reader VirtualAddressReader
        {
            get
            {
                CreateVirtualAddressReader();
                return _virtualAddressReader;
            }
        }

        public byte[] BuildId
        {
            get
            {
                if (Header.ProgramHeaderOffset != IntPtr.Zero && Header.ProgramHeaderEntrySize > 0 && Header.ProgramHeaderCount > 0)
                {
                    try
                    {
                        foreach (ElfNote note in Notes)
                            if (note.Type == ElfNoteType.PrpsInfo && note.Name.Equals("GNU"))
                                return note.ReadContents(0, (int)note.Header.ContentSize);
                    }
                    catch (IOException)
                    {
                    }
                }
                return null;
            }
        }

        public ElfFile(Reader reader, long position = 0, bool virt = false)
        {
            _reader = reader;
            _position = position;
            _virtual = virt;

            if (virt)
                _virtualAddressReader = reader;

            Header = reader.Read<ElfHeader>(position);
            Header.Validate(reader.DataSource.Name);
        }

        internal ElfFile(ElfHeader header, Reader reader, long position = 0, bool virt = false)
        {
            _reader = reader;
            _position = position;
            _virtual = virt;

            if (virt)
                _virtualAddressReader = reader;

            Header = header;
        }

        private void CreateVirtualAddressReader()
        {
            if (_virtualAddressReader != null)
                return;

            _virtualAddressReader = new Reader(new ELFVirtualAddressSpace(ProgramHeaders, _reader.DataSource));
        }

        private void LoadNotes()
        {
            if (_notes != null)
                return;

            LoadProgramHeaders();

            List<ElfNote> notes = new List<ElfNote>();
            foreach (ElfProgramHeader programHeader in _programHeaders)
            {
                if (programHeader.Header.Type == ElfProgramHeaderType.Note)
                {
                    Reader reader = new Reader(programHeader.AddressSpace);
                    long position = 0;
                    while (position < reader.DataSource.Length)
                    {
                        ElfNote note = new ElfNote(reader, position);
                        notes.Add(note);

                        position += note.TotalSize;
                    }
                }
            }

            _notes = notes.ToArray();
        }

        private void LoadProgramHeaders()
        {
            if (_programHeaders != null)
                return;

            _programHeaders = new ElfProgramHeader[Header.ProgramHeaderCount];
            for (int i = 0; i < _programHeaders.Length; i++)
                _programHeaders[i] = new ElfProgramHeader(_reader, _position + (long)Header.ProgramHeaderOffset + i * Header.ProgramHeaderEntrySize, _position, _virtual);
        }

        private string GetSectionName(int section)
        {
            LoadSections();
            if (section < 0 || section >= _sections.Length)
                throw new ArgumentOutOfRangeException(nameof(section));

            if (_sectionNames == null)
                _sectionNames = new string[_sections.Length];

            if (_sectionNames[section] != null)
                return _sectionNames[section];

            LoadSectionNameTable();
            ref ElfSectionHeader hdr = ref _sections[section];
            int idx = hdr.NameIndex;

            if (hdr.Type == ElfSectionHeaderType.Null || idx == 0)
                return _sectionNames[section] = string.Empty;

            int len = 0;
            for (len = 0; idx + len < _sectionNameTable.Length && _sectionNameTable[idx + len] != 0; len++)
                ;

            string name = Encoding.ASCII.GetString(_sectionNameTable, idx, len);
            _sectionNames[section] = name;

            return _sectionNames[section];
        }

        private byte[] _sectionNameTable;

        private void LoadSectionNameTable()
        {
            if (_sectionNameTable != null)
                return;

            int nameTableIndex = Header.SectionHeaderStringIndex;
            if (Header.SectionHeaderOffset != IntPtr.Zero && Header.SectionHeaderCount > 0 && nameTableIndex != 0)
            {
                ref ElfSectionHeader hdr = ref _sections[nameTableIndex];
                long offset = hdr.FileOffset.ToInt64();
                int size = checked((int)hdr.FileSize.ToInt64());

                _sectionNameTable = _reader.ReadBytes(offset, size);
            }
        }

        private void LoadSections()
        {
            if (_sections != null)
                return;

            _sections = new ElfSectionHeader[Header.SectionHeaderCount];
            for (int i = 0; i < _sections.Length; i++)
                _sections[i] = _reader.Read<ElfSectionHeader>(_position + (long)Header.SectionHeaderOffset + i * Header.SectionHeaderEntrySize);
        }

#if DEBUG
        private void LoadAllSectionNames()
        {
            LoadSections();

            for (int i = 0; i < _sections.Length; i++)
                GetSectionName(i);
        }
#endif
    }
}