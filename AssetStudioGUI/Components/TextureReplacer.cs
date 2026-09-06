using System;
using AssetStudio;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AssetStudioGUI
{
    /// <summary>
    /// Describes the sub-rectangle of a shared Texture2D that a Sprite's pixels occupy,
    /// plus the packing transform that needs to be undone when writing new pixels back in.
    /// Null region (from TryResolveTarget) means "replace the whole texture".
    /// </summary>
    public readonly struct ReplaceRegion
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;
        public readonly SpritePackingRotation Rotation;

        public ReplaceRegion(int x, int y, int width, int height, SpritePackingRotation rotation)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Rotation = rotation;
        }
    }

    public static class TextureReplacer
    {
        /// <summary>
        /// Resolves an asset picked in the list to the Texture2D it would replace, if any.
        /// For a Sprite that shares a texture with other sprites (atlas-packed, or only covering
        /// part of a texture), <paramref name="region"/> is set to the sprite's pixel rectangle so
        /// that only those pixels get overwritten, leaving the rest of the shared texture untouched.
        /// A small set of packing schemes (tight/polygon-clipped packing, 90°-rotated packing, and
        /// downscaled atlases) are refused because their pixel mapping isn't a simple rectangle copy
        /// and getting it wrong would corrupt the asset.
        /// </summary>
        public static bool TryResolveTarget(object asset, out Texture2D texture, out ReplaceRegion? region, out string reason)
        {
            texture = null;
            region = null;
            reason = null;

            switch (asset)
            {
                case Texture2D tex:
                    texture = tex;
                    return true;

                case Sprite sprite:
                    return TryResolveSprite(sprite, out texture, out region, out reason);

                default:
                    reason = "Only Texture2D and Sprite assets can be replaced.";
                    return false;
            }
        }

        private static bool TryResolveSprite(Sprite sprite, out Texture2D texture, out ReplaceRegion? region, out string reason)
        {
            texture = null;
            region = null;
            reason = null;

            Texture2D underlyingTexture;
            Rectf textureRect;
            float downscaleMultiplier;
            SpriteSettings settingsRaw;

            if (sprite.m_SpriteAtlas != null && sprite.m_SpriteAtlas.TryGet(out var m_SpriteAtlas))
            {
                if (!m_SpriteAtlas.m_RenderDataMap.TryGetValue(sprite.m_RenderDataKey, out var spriteAtlasData) || !spriteAtlasData.texture.TryGet(out underlyingTexture))
                {
                    reason = "Couldn't resolve this sprite's atlas texture.";
                    return false;
                }
                textureRect = spriteAtlasData.textureRect;
                downscaleMultiplier = spriteAtlasData.downscaleMultiplier;
                settingsRaw = spriteAtlasData.settingsRaw;
            }
            else
            {
                if (sprite.m_RD?.texture == null || !sprite.m_RD.texture.TryGet(out underlyingTexture))
                {
                    reason = "Couldn't resolve this sprite's texture.";
                    return false;
                }
                textureRect = sprite.m_RD.textureRect;
                downscaleMultiplier = sprite.m_RD.downscaleMultiplier;
                settingsRaw = sprite.m_RD.settingsRaw;
            }

            if (settingsRaw.packingMode == SpritePackingMode.Tight)
            {
                reason = "This sprite uses tight (polygon-clipped) packing, so its footprint isn't a plain rectangle. " +
                          "Direct pixel replacement isn't supported for tight-packed sprites - open the atlas's Texture2D asset directly instead.";
                return false;
            }

            var rotation = settingsRaw.packed == 1 ? settingsRaw.packingRotation : SpritePackingRotation.None;
            if (rotation == SpritePackingRotation.Rotate90)
            {
                reason = "This sprite is packed rotated 90° within its atlas, which isn't supported for direct pixel replacement yet. " +
                          "Open the atlas's Texture2D asset directly instead.";
                return false;
            }

            if (downscaleMultiplier > 0f && downscaleMultiplier != 1f)
            {
                reason = "This sprite atlas uses a downscale multiplier, which isn't supported for direct pixel replacement yet.";
                return false;
            }

            var rectX = (int)MathF.Floor(textureRect.x);
            var rectY = (int)MathF.Floor(textureRect.y);
            var rectRight = Math.Min((int)MathF.Ceiling(textureRect.x + textureRect.width), underlyingTexture.m_Width);
            var rectBottom = Math.Min((int)MathF.Ceiling(textureRect.y + textureRect.height), underlyingTexture.m_Height);
            var width = rectRight - rectX;
            var height = rectBottom - rectY;

            if (rectX < 0 || rectY < 0 || width <= 0 || height <= 0)
            {
                reason = "Couldn't determine a valid pixel region for this sprite.";
                return false;
            }

            texture = underlyingTexture;
            region = new ReplaceRegion(rectX, rectY, width, height, rotation);
            return true;
        }

        /// <summary>Whether replacement is possible for this texture, and why not if it isn't.</summary>
        public static bool CanReplace(Texture2D tex, out string reason)
        {
            reason = null;

            if (tex.platform == BuildTarget.Switch && tex.m_PlatformBlob != null && tex.m_PlatformBlob.Length != 0)
            {
                reason = "Switch-swizzled textures aren't supported for replacement.";
                return false;
            }

            if (!Texture2DEncoder.IsFormatSupported(tex.m_TextureFormat))
            {
                reason = $"Replacing {tex.m_TextureFormat} textures isn't supported.\nSupported formats: {string.Join(", ", Texture2DEncoder.SupportedFormats)}.";
                return false;
            }

            if (tex.image_data == null || tex.image_data.Size == 0)
            {
                reason = "This texture has no image data to replace (it may be a placeholder or a texture-array slice).";
                return false;
            }

            if (tex.image_data.GetPatchableFilePath() == null)
            {
                reason = "This texture's data lives inside a compressed asset bundle held in memory, so it can't be patched on disk directly.\n\n" +
                         "Extract the bundle to loose files first (File > Extract file / Extract folder), then load the extracted files and try again.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Replaces <paramref name="tex"/>'s image data with the contents of <paramref name="imagePath"/>,
        /// writing the change directly to the backing file on disk. If <paramref name="region"/> is given,
        /// only that rectangle of pixels is overwritten - the rest of the (possibly shared/atlased) texture
        /// is left untouched. Throws with a user-facing message on any failure; does not modify anything if
        /// it can't succeed.
        /// </summary>
        public static void Replace(Texture2D tex, string imagePath, ReplaceRegion? region = null)
        {
            if (!CanReplace(tex, out var reason))
            {
                throw new InvalidOperationException(reason);
            }

            using (var newImage = Image.Load<Bgra32>(imagePath))
            {
                if (region == null)
                {
                    if (newImage.Width != tex.m_Width || newImage.Height != tex.m_Height)
                    {
                        newImage.Mutate(x => x.Resize(tex.m_Width, tex.m_Height));
                    }

                    // Raw Unity texture data is stored bottom-up; a normally-loaded image file is top-down.
                    newImage.Mutate(x => x.Flip(FlipMode.Vertical));

                    EncodeAndPatch(tex, newImage);
                    return;
                }

                var r = region.Value;
                if (newImage.Width != r.Width || newImage.Height != r.Height)
                {
                    newImage.Mutate(x => x.Resize(r.Width, r.Height));
                }

                // Undo the top-down -> bottom-up flip that CutImage applies last when producing a
                // preview/export image, to get back to Unity's raw bottom-up pixel orientation.
                newImage.Mutate(x => x.Flip(FlipMode.Vertical));

                // Undo whichever packing transform CutImage applied to un-pack this sprite, so the
                // pixels line up with what's actually stored in the shared atlas texture.
                switch (r.Rotation)
                {
                    case SpritePackingRotation.FlipHorizontal:
                        newImage.Mutate(x => x.Flip(FlipMode.Horizontal));
                        break;
                    case SpritePackingRotation.FlipVertical:
                        newImage.Mutate(x => x.Flip(FlipMode.Vertical));
                        break;
                    case SpritePackingRotation.Rotate180:
                        newImage.Mutate(x => x.Rotate(180));
                        break;
                }

                using (var fullImage = tex.ConvertToImage(false))
                {
                    if (fullImage == null)
                    {
                        throw new InvalidOperationException("Couldn't decode the existing texture to composite the replacement into it.");
                    }

                    fullImage.Mutate(x => x.DrawImage(newImage, new Point(r.X, r.Y), 1f));
                    EncodeAndPatch(tex, fullImage);
                }
            }
        }

        private static void EncodeAndPatch(Texture2D tex, Image<Bgra32> image)
        {
            if (!Texture2DEncoder.TryEncode(image, tex.m_TextureFormat, out var data, out var encodeError))
            {
                throw new InvalidOperationException(encodeError);
            }

            if (data.Length != tex.image_data.Size)
            {
                throw new InvalidOperationException(
                    $"Encoded data size ({data.Length} bytes) doesn't match the original ({tex.image_data.Size} bytes) - refusing to patch to avoid corrupting the file.");
            }

            if (!tex.image_data.TryPatchData(data))
            {
                throw new InvalidOperationException("Failed to write the new texture data to disk.");
            }
        }
    }
}
