# Como abrir o DirectPackageInstaller a partir do código-fonte

Este guia explica como compilar e abrir a versão Desktop (Windows, Linux e macOS) para desenvolver e testar a interface.

## Início rápido

```bash
git clone https://github.com/edupoko/DirectPackageInstalle-NG.git
cd DirectPackageInstalle-NG
git switch DN8
scripts/run.sh
```

O script localiza o SDK do .NET, inicializa os submódulos se faltarem, compila e abre a janela do app.

## Requisitos

| Requisito | Detalhe |
|---|---|
| **SDK do .NET 8** | O `global.json` exige o SDK 8. Ter só o runtime não basta, e um SDK 9 ou 10 sozinho não atende. |
| **Git** | Para clonar e inicializar os submódulos. |
| **Sessão gráfica** | O app abre uma janela, então precisa de um ambiente desktop (não funciona em SSH sem display). |

### Instalar o SDK do .NET 8 sem sudo

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet8"
```

O `scripts/run.sh` já procura o SDK em `~/.dotnet8`, então não é preciso alterar o `PATH`.

## Usando o script

```bash
scripts/run.sh                 # compila (Debug) e abre o app
scripts/run.sh --build-only    # só compila, sem abrir
scripts/run.sh --release       # compila em Release
scripts/run.sh -- <args>       # repassa argumentos ao app
scripts/run.sh --help
```

O script procura o SDK nesta ordem: `DOTNET_ROOT`, `~/.dotnet8` e o `dotnet` do sistema.

## Sem o script (comandos manuais)

```bash
export PATH="$HOME/.dotnet8:$PATH"
export DOTNET_ROOT="$HOME/.dotnet8"

git submodule update --init --depth 1

dotnet build DirectPackageInstaller/DirectPackageInstaller.Desktop/DirectPackageInstaller.Desktop.csproj -c Debug
dotnet run --project DirectPackageInstaller/DirectPackageInstaller.Desktop
```

No fish, use `set -x PATH $HOME/.dotnet8 $PATH` e `set -x DOTNET_ROOT $HOME/.dotnet8`.

## O que conferir na interface

- As telas mantêm o fundo azul-escuro: tela principal, seletor de arquivos, lista de links, caixas de mensagem e gerenciador de cookies.
- A cor de fundo está em um só lugar: o recurso `AppBackgroundBrush` em `DirectPackageInstaller/DirectPackageInstaller/App.axaml`.
- Colar uma URL na barra superior muda o botão de "Open" para "Load".
- Arrastar um arquivo `.pkg` para a janela carrega o preview.

## Problemas comuns

| Sintoma | Causa provável | Solução |
|---|---|---|
| `SDK do .NET 8 não encontrado` | Só há runtime ou outra versão do SDK | Instale o SDK 8 como mostrado acima |
| Erros de projeto ausente (`LibOrbisPkg`, `HttpServerLite`, `DHCPServer`) | Submódulos não inicializados | `git submodule update --init --depth 1` |
| A janela não abre | Sem sessão gráfica | Rode em um ambiente desktop |
| `A compatible .NET SDK was not found` | O `global.json` pede SDK 8 e o ativo é outro | Defina `DOTNET_ROOT` para a pasta do SDK 8 |

## Outras plataformas

- **Android, iOS e Web** exigem workloads e ferramentas adicionais (por exemplo, `dotnet workload install android` e o NDK). Para esses alvos, o caminho mais prático é o build do CI (`Build.cmd` e `.github/workflows/main.yml`).
- Os binários prontos de cada run do CI ficam nos artefatos da execução, na aba **Actions** do repositório.
