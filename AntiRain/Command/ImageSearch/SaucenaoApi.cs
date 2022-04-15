﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AntiRain.Config;
using AntiRain.Tool;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PyLibSharp.Requests;
using SixLabors.ImageSharp;
using Sora.Entities;
using Sora.Entities.Segment;
using YukariToolBox.LightLog;

namespace AntiRain.Command.ImageSearch;

public static class SaucenaoApi
{
    public static async ValueTask<MessageBody> SearchByUrl(string apiKey, string url, long selfId)
    {
        Log.Debug("pic search", "send api request"); 
        JToken res;
        try
        {
            var serverResponse = await
                Requests.PostAsync($"http://saucenao.com/search.php?output_type=2&numres=16&db=999&api_key={apiKey}&url={url}",
                                   new ReqParams
                                   {
                                       Timeout                = 20000,
                                       IsThrowErrorForTimeout = false
                                   });
            res = serverResponse.Json();
        }
        catch (Exception e)
        {
            Log.Error("NetError", Log.ErrorLogBuilder(e));
            return $"服务器网络错误{e.Message}";
        }

        var resCode = Convert.ToInt32(res?["header"]?["status"] ?? -1);
        Log.Debug("pic", $"get api result code [{resCode}]");

        //API返回失败
        if (res is null || resCode != 0) return "图片获取失败";

        //API返回空值
        if (res["results"] is not JArray resData) return "处理API返回发生错误";

        //未找到图片
        if (resData.Count == 0) return "查找结果为空";

        var parsedPic = resData.First();

        if (!ConfigManager.TryGetUserConfig(selfId, out var userConfig))
        {
            //用户配置获取失败
            Log.Error("Config|SaucenaoApi", "无法获取用户配置文件");
            return "处理用户配置发生错误\r\nMessage:无法读取用户配置";
        }

        int databaseId = Convert.ToInt32(parsedPic["header"]?["index_id"]);
        Log.Debug("pic search", $"get pic type:{databaseId}");
        switch (databaseId)
        {
            //pixiv
            case 5:
            {
                var pid      = Convert.ToInt64(parsedPic["data"]?["pixiv_id"]);
                var imageUrl = MediaUtil.GenPixivUrl(userConfig.HsoConfig.PximyProxy, pid);
                return GenPixivResult(imageUrl, pid, parsedPic);
            }
            //twitter
            case 41:
            {
                var tweetUrl = parsedPic["data"]?["ext_urls"]?[0]?.ToString();
                return string.IsNullOrEmpty(tweetUrl)
                    ? "服务器歇逼了（无法获取推文链接，请稍后再试）"
                    : GenTwitterResult(tweetUrl, userConfig.TwitterApiToken, parsedPic);
            }
            //eh
            case 38:
                return "[Saucenao-EHentai]"                         +
                       $"\r\nSource:{parsedPic["data"]?["source"]}" +
                       $"\r\nName:{parsedPic["data"]?["jp_name"]}"  +
                       $"\r\n[{parsedPic["header"]?["similarity"]}%]";
            case 18:
                return "[Saucenao-NHentai]"                         +
                       $"\r\nSource:{parsedPic["data"]?["source"]}" +
                       $"\r\nName:{parsedPic["data"]?["jp_name"]}"  +
                       $"\r\n[{parsedPic["header"]?["similarity"]}%]";
            //unknown
            default:
            {
                //检查源
                string source = parsedPic["data"]?["source"]?.ToString() ?? string.Empty;

                //包含pixiv链接
                if (source.IndexOf("pixiv", StringComparison.Ordinal) != -1 &&
                    long.TryParse(Path.GetFileName(source), out long pid))
                {
                    var imageUrl = MediaUtil.GenPixivUrl(userConfig.HsoConfig.PximyProxy, pid);
                    return GenPixivResult(imageUrl, pid, parsedPic);
                }

                //包含twitter链接
                if (source.IndexOf("twitter", StringComparison.Ordinal) != -1)
                    return GenTwitterResult(source, userConfig.TwitterApiToken, parsedPic);

                Log.Debug("pic search", $"get unknown source:{source}");
                string b64Pic =
                    MediaUtil.DrawTextImage(parsedPic["data"]?.ToString(Formatting.Indented) ?? string.Empty,
                                            Color.Black, Color.White);

                var msg = new MessageBody
                {
                    $"[Saucenao-UnknownDatabase]\r\n{parsedPic["header"]?["index_name"]}",
                    SoraSegment.Image($"base64://{b64Pic}"),
                    $"\r\n[{parsedPic["header"]?["similarity"]}%]"
                };

                return msg;
            }
        }
    }

    private static MessageBody GenTwitterResult(string tweetUrl, string token, JToken apiRet)
    {
        string tId = Path.GetFileName(tweetUrl);
        (bool success, string sender, string text, List<string> media) = MediaUtil.GetTweet(tId, token);

        if (!success) return $"推特API错误\r\nMessage:{text}";

        var msg = new MessageBody
        {
            $"[Saucenao-Twitter]\r\n推文:{text}\r\n用户:{sender}\r\n"
        };
        foreach (var pic in media) msg.Add(SoraSegment.Image(pic));

        msg.Add($"\r\nLink:{tweetUrl}");
        msg.Add($"\r\n[{apiRet["header"]?["similarity"]}%]");
        return msg;
    }

    private static MessageBody GenPixivResult(string url, long pid, JToken apiRet)
    {
        (int statusCode, bool r18, int count) = MediaUtil.GetPixivImgInfo(pid, out JToken json);
        if (statusCode != 200) return $"[网络错误{statusCode}]";
        MessageBody   msg = new();
        StringBuilder sb  = new();

        sb.AppendLine("[Saucenao-Pixiv]");
        sb.AppendLine($"图片名:{json["body"]?["title"]}");
        sb.Append($"作者:{json["body"]?["userName"]}");
        msg.Add(sb.ToString());
        sb.Clear();

        if (r18) msg.Add($"[H是不行的]{Environment.NewLine}");
        else msg.Add(SoraSegment.Image(url));

        sb.AppendLine($"Pixiv Id:{pid}");
        sb.Append($"[{apiRet["header"]?["similarity"]}%]");
        if (count != 1)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("有多张图片，使用下面的指令查看合集图片");
            sb.Append($"[让我康康{pid}]");
        }
        msg.Add(sb.ToString());

        return msg;
    }
}