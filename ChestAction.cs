// HorizonChests - TShock plugin: database-backed chests / cofres en base de datos
// Copyright (C) 2026 Riburitu
//
// Based on InfiniteChests V3 by Zaicon, which is in turn
// based on InfiniteChests by MarioE. / Basado en InfiniteChests V3 de Zaicon,
// que a su vez se basa en InfiniteChests de MarioE.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace HorizonChests
{
    public enum ChestAction
    {
        None,
        GetInfo,
        Protect,
        TogglePublic,
        SetGroup,
        SetUser,
        SetRefill,
        Unprotect,
        ToggleBreakable,
        EditMaster
    }
}