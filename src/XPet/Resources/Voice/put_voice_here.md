把生成好的语音文件放在本目录（Resources/Voice/）下。

命名必须严格一致（小写 + 下划线），格式建议 .wav 或 .mp3：


|文件名|触发时机|建议文案|
|---|---|---|
|`hello.wav`|启动问候|你好呀～|
|`ingest_start.wav`|入库开始监视|开始监视下载文件夹啦|
|`ingest_ok.wav`|入库成功|入库成功！|
|`ingest_skip.wav`|已存在跳过|之前已经入库过了|
|`ingest_warn.wav`|成功但有媒体问题|入库了，但媒体有点问题|
|`ingest_error.wav`|出错 / 失败|出错了……|
|`ingest_stop.wav`|手动停止入库|入库已停止|
|`ingest_exit.wav`|进程异常退出|入库程序退出了|
|`click.wav`|单击宠物|（轻提示音 / 「嗯？」）|
|`config_reload.wav`|重新载入配置|配置已重新载入|


生成建议：短句、可爱女声/萝莉声，每条 1～3 秒。
