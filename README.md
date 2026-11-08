# YouTubeMP3 Windows - 1 EXE 배포 설정

이 프로젝트는 기존 Windows C# WPF 코드에 **win-x64 / SelfContained / PublishSingleFile** 설정을 추가한 버전입니다. C# 코드는 기존 4칸 들여쓰기를 유지합니다.

## 단일 실행 파일 만드는 방법

1. Windows PC에 Visual Studio 2022(또는 최신) 및 **.NET 8 SDK / .NET 데스크톱 개발**을 설치합니다.
2. `Publish-OneEXE.bat`를 더블 클릭합니다. 또는 프로젝트 파일을 Visual Studio에서 열어 Release 게시합니다.
3. 게시가 성공하면 `publish\win-x64\YouTubeMP3.Windows.exe`를 확인합니다.
4. 게시 폴더에 추가 필수 파일이 없는지 확인한 후 EXE 한 개만 배포합니다.

수동 실행 명령:

```powershell
dotnet publish .\YouTubeMP3.Windows\YouTubeMP3.Windows.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false
```

### 배포 범위와 주의점

- .NET 런타임은 EXE 안에 포함합니다. 대상 PC에 .NET SDK 설치는 필요하지 않습니다.
- WebView2는 Edge WebView2 Runtime을 사용합니다. 없는 Windows PC에는 별도 설치가 필요할 수 있습니다.
- `yt-dlp.exe`는 검색 첫 실행 시, `ffmpeg.exe`는 MP3 첫 변환 시 각각 인터넷으로 다운로드되어 `%LOCALAPPDATA%\YouTubeMP3\Tools`에 저장됩니다. 즉, **파일 한 개로 배포**하지만 앱 실행 후 보조 파일은 생성됩니다.
- PublishSingleFile은 필요한 라이브러리를 실행 시 내부 캐시로 추출할 수 있습니다. 이 동작은 정상입니다.
- Windows 64비트 대상입니다. ARM64용은 별도 RID/빌드가 필요합니다.
- 이 환경에서는 Windows WPF 컴파일을 실행할 수 없어 실제 EXE는 생성하지 못했으며, 수정된 프로젝트와 게시 스크립트를 제공합니다.
- 배포 전 다른 PC에서 미리보기, 검색, 다운로드, 변환을 테스트하세요. 콘텐츠에 대한 다운로드 권한과 관련 소프트웨어 라이선스를 확인하세요.

### 단일 파일 설정 (.csproj)

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

기존 아이콘 설정, 오류 153 우회, `System.IO` 컴파일 수정 및 FFmpeg 자동 다운로드 기능을 그대로 유지합니다.
