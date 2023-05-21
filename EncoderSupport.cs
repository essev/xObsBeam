﻿using System;
using System.Runtime.CompilerServices;
using ObsInterop;
using LibJpegTurbo;

namespace xObsBeam
{
  enum Encoders
  {
    LibJpegTurboV2,
    LibJpegTurboV3,
  }

  public static class EncoderSupport
  {
    static readonly Dictionary<Encoders, bool> _checkResults = new();

    public static unsafe bool LibJpegTurbo
    {
      get
      {
        var encoder = Encoders.LibJpegTurboV2;
        if (!_checkResults.ContainsKey(encoder))
        {
          try
          {
            TurboJpeg.tjDestroy(TurboJpeg.tjInitCompress());
            _checkResults.Add(encoder, true);
          }
          catch (System.Exception ex)
          {
            _checkResults.Add(encoder, false);
            Module.Log($"{encoder} encoder availability check failed with {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", ObsLogLevel.Debug);
          }
          Module.Log($"{encoder} encoder is " + (_checkResults[encoder] ? "available." : "not available."), ObsLogLevel.Info);
        }
        return _checkResults[encoder];
      }
    }

    public static unsafe bool LibJpegTurboV3
    {
      get
      {
        var encoder = Encoders.LibJpegTurboV3;
        if (!_checkResults.ContainsKey(encoder))
        {
          try
          {
            TurboJpeg.tj3Destroy(TurboJpeg.tj3Init((int)TJINIT.TJINIT_COMPRESS));
            _checkResults.Add(encoder, true);
          }
          catch (System.Exception ex)
          {
            _checkResults.Add(encoder, false);
            Module.Log($"{encoder} encoder availability check failed with {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", ObsLogLevel.Debug);
          }
          Module.Log($"{encoder} encoder is " + (_checkResults[encoder] ? "available." : "not available."), ObsLogLevel.Info);
        }
        return _checkResults[encoder];
      }
    }
    public static unsafe bool LibJpegTurboLossless => LibJpegTurboV3;

    // check format_is_yuv function in OBS video-io.h for reference: https://github.com/obsproject/obs-studio/blob/master/libobs/media-io/video-io.h
    public static bool FormatIsYuv(video_format format)
    {
      switch (format)
      {
        case video_format.VIDEO_FORMAT_I420:
        case video_format.VIDEO_FORMAT_NV12:
        case video_format.VIDEO_FORMAT_I422:
        case video_format.VIDEO_FORMAT_I210:
        case video_format.VIDEO_FORMAT_YVYU:
        case video_format.VIDEO_FORMAT_YUY2:
        case video_format.VIDEO_FORMAT_UYVY:
        case video_format.VIDEO_FORMAT_I444:
        case video_format.VIDEO_FORMAT_I412:
        case video_format.VIDEO_FORMAT_I40A:
        case video_format.VIDEO_FORMAT_I42A:
        case video_format.VIDEO_FORMAT_YUVA:
        case video_format.VIDEO_FORMAT_YA2L:
        case video_format.VIDEO_FORMAT_AYUV:
        case video_format.VIDEO_FORMAT_I010:
        case video_format.VIDEO_FORMAT_P010:
          // OBS 29.1.X+
          // case video_format.VIDEO_FORMAT_P216:
          // case video_format.VIDEO_FORMAT_P416:
          // case video_format.VIDEO_FORMAT_V210:
          return true;
        case video_format.VIDEO_FORMAT_NONE:
        case video_format.VIDEO_FORMAT_RGBA:
        case video_format.VIDEO_FORMAT_BGRA:
        case video_format.VIDEO_FORMAT_BGRX:
        case video_format.VIDEO_FORMAT_Y800:
        case video_format.VIDEO_FORMAT_BGR3:
          return false;
      }
      return false;
    }

    public static TJPF ObsToJpegPixelFormat(video_format obsVideoFormat) => obsVideoFormat switch
    {
      video_format.VIDEO_FORMAT_BGR3 => TJPF.TJPF_BGR,
      video_format.VIDEO_FORMAT_BGRA => TJPF.TJPF_BGRA,
      video_format.VIDEO_FORMAT_BGRX => TJPF.TJPF_BGRX,
      video_format.VIDEO_FORMAT_RGBA => TJPF.TJPF_RGBA,
      video_format.VIDEO_FORMAT_Y800 => TJPF.TJPF_GRAY,
      _ => TJPF.TJPF_UNKNOWN
    };

    public static TJCS ObsToJpegColorSpace(video_format obsVideoFormat)
    {
      if (obsVideoFormat == video_format.VIDEO_FORMAT_Y800)
        return TJCS.TJCS_GRAY;
      return (FormatIsYuv(obsVideoFormat)) ? TJCS.TJCS_YCbCr : TJCS.TJCS_RGB;
    }

    public static TJSAMP ObsToJpegSubsampling(video_format obsVideoFormat)
    {
      switch (obsVideoFormat)
      {
        case video_format.VIDEO_FORMAT_Y800:
          return TJSAMP.TJSAMP_GRAY;
        case video_format.VIDEO_FORMAT_I420:
        case video_format.VIDEO_FORMAT_I40A:
        case video_format.VIDEO_FORMAT_I010:
        case video_format.VIDEO_FORMAT_NV12:
        case video_format.VIDEO_FORMAT_P010:
          return TJSAMP.TJSAMP_420;
        case video_format.VIDEO_FORMAT_I422:
        case video_format.VIDEO_FORMAT_I42A:
        case video_format.VIDEO_FORMAT_YVYU:
        case video_format.VIDEO_FORMAT_YUY2:
        case video_format.VIDEO_FORMAT_UYVY:
        case video_format.VIDEO_FORMAT_I210:
          // case video_format.VIDEO_FORMAT_P216: // OBS 29.1.X+
          // case video_format.VIDEO_FORMAT_V210: // OBS 29.1.X+
          return TJSAMP.TJSAMP_422;
        case video_format.VIDEO_FORMAT_I412:
        case video_format.VIDEO_FORMAT_I444:
        case video_format.VIDEO_FORMAT_AYUV:
        case video_format.VIDEO_FORMAT_YUVA:
        case video_format.VIDEO_FORMAT_YA2L:
          // case video_format.VIDEO_FORMAT_P416: // OBS 29.1.X+
          return TJSAMP.TJSAMP_444;
        case video_format.VIDEO_FORMAT_NONE:
        case video_format.VIDEO_FORMAT_RGBA:
        case video_format.VIDEO_FORMAT_BGRA:
        case video_format.VIDEO_FORMAT_BGRX:
        case video_format.VIDEO_FORMAT_BGR3:
          return TJSAMP.TJSAMP_444;
      }
      return TJSAMP.TJSAMP_444;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void Nv12ToJpeg(byte* sourceBuffer, Span<byte> destinationBuffer, uint[] planeSizes)
    {
      // copy the Y plane
      new ReadOnlySpan<byte>(sourceBuffer, (int)planeSizes[0]).CopyTo(destinationBuffer);

      // copy and deinterleave the UV plane
      byte* uvPlane = sourceBuffer + planeSizes[0];
      int uvPlaneSize = (int)planeSizes[1] / 2;
      var uPlane = destinationBuffer.Slice((int)planeSizes[0], uvPlaneSize);
      var vPlane = destinationBuffer.Slice((int)planeSizes[0] + uvPlaneSize, uvPlaneSize);
      for (int i = 0; i < uvPlaneSize; i++)
      {
        uPlane[i] = uvPlane[(2 * i) + 0];
        vPlane[i] = uvPlane[(2 * i) + 1];
      }
    }

  }

}