`dolby-vision-rpu.bin` 是一个 438 字节的 HEVC Dolby Vision RPU NAL 单元，仅含动态映射元数据，不含视频图像或音频。它用于验证去除转义字节后的 RPU 头和 CRC 校验，以及缺少杜比视界容器配置的 MP4 回退识别。测试在内存中生成 MP4 样本表，不依赖外部视频、ffprobe 或下载。
# Classification fixtures

`classification.ass` is synthetic text authored for this project's tests: 12 sequential bottom-center dialogue events, 12 horizontal scrolling events, and 3 distinct animation events. `classification.mkv` wraps the exact ASS using subtitle stream copy. These contain no external media; regenerate with `scripts/generate-caption-fixtures.ps1`.
