# 可寻址资源
大部分**资源**都使用**可寻址资源**来查找,**不直接保存**在游戏文件/目录里

## 歌曲
每首歌曲资源存放在 `Assets/Tracks/{song_id}` 下  

例如 `Assets/Tracks/Aleph0.LeaF.0` 会有以下内容：  
### Music (音乐)
- `music.wav`  
可以访问[`http://127.0.0.1:41669/asset/music?path=Assets/Tracks/Aleph0.LeaF.0/music.wav`](http://127.0.0.1:41669/asset/music?path=Assets/Tracks/Aleph0.LeaF.0/music.wav)查看

### Illustrations (曲绘文件)
- `Illustration.jpg` – 主封面图  
可以访问[`http://127.0.0.1:41669/asset/image?path=Assets/Tracks/Aleph0.LeaF.0/Illustration.jpg`](http://127.0.0.1:41669/asset/image?path=Assets/Tracks/Aleph0.LeaF.0/Illustration.jpg)下载
- `IllustrationLowRes.jpg` – 低分辨率封面图
- `IllustrationBlur.jpg` – 模糊封面图

### Charts (谱面文件)
- `Chart_EZ.json`  
  可以访问[`http://127.0.0.1:41669/asset/text?path=Assets/Tracks/Aleph0.LeaF.0/Chart_EZ.json`](http://127.0.0.1:41669/asset/text?path=Assets/Tracks/Aleph0.LeaF.0/Chart_EZ.json)查看
- `Chart_HD.json`
- `Chart_IN.json`
- `Chart_AT.json`
- `Chart_Legacy.json`

Tip: 大部分歌曲`AT`和`Legacy`难度并不存在  
Tip: 冷知识`Random`的`song_id`是`Random.SobremSilentroom.{num}`,并且只存储铺面文件

## 章节封面
`Assets/Tracks/#ChapterCover/{code}.jpg`