using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SRA
{
    public static class GlobalAttackMapLabelUtility
    {
        public static string GetMapLabel(Map map)
        {
            string parentLabel = GetParentLabel(map);
            string detailLabel = GetDetailLabel(map, parentLabel);
            string mapKind = map != null && map.IsPocketMap
                ? "SRA_RemoteArtillery_TargetMapKind_Pocket".Translate()
                : "SRA_RemoteArtillery_TargetMapKind_Surface".Translate();
            string tileLabel = GetTileLabel(map);
            string sizeLabel = GetSizeLabel(map);
            int mapId = map != null ? map.uniqueID : -1;

            if (!detailLabel.NullOrEmpty() && detailLabel != parentLabel)
            {
                return "SRA_RemoteArtillery_TargetMapMenuLabel_WithDetail".Translate(parentLabel, detailLabel, mapKind, mapId, tileLabel, sizeLabel);
            }

            return "SRA_RemoteArtillery_TargetMapMenuLabel".Translate(parentLabel, mapKind, mapId, tileLabel, sizeLabel);
        }

        private static string GetParentLabel(Map map)
        {
            if (map?.info?.parent != null)
            {
                return map.info.parent.Label;
            }

            return "Map".Translate();
        }

        private static string GetDetailLabel(Map map, string parentLabel)
        {
            if (map == null)
            {
                return parentLabel;
            }

            if (map.IsPocketMap && map.generatorDef != null)
            {
                string generatorLabel = map.generatorDef.LabelCap.ToString();
                if (!generatorLabel.NullOrEmpty())
                {
                    return generatorLabel;
                }
            }

            if (map.Biome != null)
            {
                string biomeLabel = map.Biome.LabelCap.ToString();
                if (!biomeLabel.NullOrEmpty())
                {
                    return biomeLabel;
                }
            }

            return parentLabel;
        }

        private static string GetTileLabel(Map map)
        {
            if (map != null && IsValidWorldTile(map.Tile))
            {
                return "SRA_RemoteArtillery_TargetMapTile".Translate((int)map.Tile);
            }

            return "SRA_RemoteArtillery_TargetMapNoTile".Translate();
        }

        private static string GetSizeLabel(Map map)
        {
            if (map == null)
            {
                return string.Empty;
            }

            return "SRA_RemoteArtillery_TargetMapSize".Translate(map.Size.x, map.Size.z);
        }

        public static bool IsValidWorldTile(PlanetTile tile)
        {
            return tile.Valid && Find.WorldGrid != null && (int)tile >= 0 && (int)tile < Find.WorldGrid.TilesCount;
        }
    }
}
