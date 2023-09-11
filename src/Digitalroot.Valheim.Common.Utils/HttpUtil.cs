using JetBrains.Annotations;
using System;
using System.IO;
using System.Net.Http;

namespace Digitalroot.Valheim.Common;

/// <summary>
/// Source: https://dev.to/1001binary/download-file-using-httpclient-wrapper-asynchronously-1p6
/// </summary>
public static class HttpUtil
{
  private static readonly HttpClient _httpClient = new();

  [UsedImplicitly]
  public static async void DownloadFileAsync(string uri, string outputPath)
  {
    File.WriteAllBytes(outputPath, await _httpClient.GetByteArrayAsync(uri));  
  }

  public static void DownloadFileAsync(Uri uri, FileInfo outputPath) => DownloadFileAsync(uri.OriginalString, outputPath.FullName);

  public static void DownloadFileAsync(Uri uri, string outputPath) => DownloadFileAsync(uri.OriginalString, outputPath);

  public static void DownloadFileAsync(string uri, FileInfo outputPath) => DownloadFileAsync(uri, outputPath.FullName);

}
