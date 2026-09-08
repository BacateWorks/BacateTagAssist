# 🥑 BacateTagAssist

Renomeador de filmes, episódios e temporadas. Você preenche os dados do release, confere a prévia e aplica os nomes.

## Baixar

Baixe o **BacateTagAssist-Portable.zip** em [Releases](https://github.com/BacateWorks/BacateTagAssist/releases/latest). Extraia tudo e abra **BacateTagAssist.exe**. Mantenha a pasta `wwwroot` junto do executável.

Abre em janela própria, sem terminal e sem aba no navegador. Windows 64 bits com [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). Não precisa instalar .NET para usar o portable.

## Como usar

1. Escolha a pasta do filme ou da temporada.
2. Selecione Filme, Season pack ou Episódio.
3. Preencha título, temporada, resolução, origem, codecs e grupo.
4. Confira os nomes da pasta e dos arquivos na prévia.
5. Clique em **Renomear selecionados**.

Teste primeiro em cópias. O app também renomeia a pasta principal, mesmo com apenas parte dos arquivos selecionada. **Desfazer última** tenta restaurar a operação mais recente; não substitui backup e pode falhar se os arquivos forem movidos ou os nomes anteriores estiverem ocupados.

## Episódios e arquivos

Com a temporada S01 preenchida, `1.mkv` vira S01E01, `10.mkv` vira S01E10 e `EP 3.mkv` vira S01E03 na identificação. O nome completo inclui os demais campos escolhidos. Confira sempre a numeração; arquivos sem identificação ficam para revisão manual em season packs.

Vídeos: `.mkv`, `.mp4`, `.avi`, `.mov`, `.m4v`, `.ts`, `.m2ts`, `.webm`.

Legendas: `.srt`, `.ass`, `.ssa`, `.vtt`.

Busca até 5.000 arquivos nas subpastas. Ignora estruturas BDMV, CERTIFICATE e VIDEO_TS. Não analisa codecs, não converte mídia e não usa FFmpeg ou MediaInfo. Os dados técnicos são preenchidos manualmente.

As nomenclaturas usam o guia do CapybaraBR. Para outros trackers, confira as regras do site. O guia de uso completo está dentro do app.

## Dados locais

O programa não envia seus arquivos para um servidor. O histórico e os dados do WebView2 ficam em `%LOCALAPPDATA%\BacateTagAssist`, fora da pasta portable. Não acompanham o download.

## Compilar

No Windows, com o SDK .NET 10:

```powershell
dotnet publish RenomeadorUploads.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o dist/BacateTagAssist
```

Distribua a pasta gerada inteira. Esta versão é para Windows; ainda não há edição Docker.

## Contribuir

Encontrou um problema? Abra uma issue com os passos para reproduzir e exemplos fictícios de nomes. Não envie caminhos pessoais, histórico ou seus arquivos de mídia. Pull requests são bem-vindos.

Código sob licença MIT. Criado por **Bacate**.
