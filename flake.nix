{
  description = "Voice2Action dev environment";

  inputs = {
    # Use a recent nixpkgs; allow user to update later.
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable"; # Using unstable to ensure .NET SDK 9 availability (FR008).
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = {
    self,
    nixpkgs,
    flake-utils,
  }:
    flake-utils.lib.eachDefaultSystem (system: let
      pkgs = import nixpkgs {inherit system;};
    in {
      formatter = pkgs.alejandra;

      # Full-featured interactive dev shell (pulls secrets via 1Password CLI)
      devShells.default = pkgs.mkShell {
        name = "voice2action-dev";
        buildInputs = with pkgs; [
          dotnet-sdk_9
          zsh
          oh-my-zsh
          jq
        ];
        DOTNET_CLI_TELEMETRY_OPTOUT = 1;
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1;
        DOTNET_NOLOGO = 1;
        DOTNET_MULTILEVEL_LOOKUP = 0;
        shellHook = ''
          # Populate secrets for interactive development (omit in CI / build shell)
          export AZURE_OPENAI_API_KEY=$(op item get "Azure OpenAI API Key" --vault Private --fields label=key --format json | jq -r '.value')
          export AZURE_OPENAI_ENDPOINT=$(op item get "Azure OpenAI API Key" --vault Private --fields label=url --format json | jq -r '.value')

          # --- Isolated zsh configuration (ZDOTDIR) ---
          export ZDOTDIR="$PWD/.nix-zsh"
          mkdir -p "$ZDOTDIR"

          # oh-my-zsh packaged path
          export OMZ_PATH="${pkgs.oh-my-zsh}/share/oh-my-zsh"
          export ZSH="$OMZ_PATH"
          export ZSH_CACHE_DIR="$ZDOTDIR/.cache"
          mkdir -p "$ZSH_CACHE_DIR"

          cat > "$ZDOTDIR/.zshenv" <<-'EOF'
            # Isolated .zshenv for Voice2Action dev shell
            export DOTNET_ROOT="${pkgs.dotnet-sdk_9}/share/dotnet"
            export ZDOTDIR="$ZDOTDIR"
            export ZSH="$ZSH"
            export ZSH_CACHE_DIR="$ZSH_CACHE_DIR"
            export DISABLE_AUTO_UPDATE="true"
          EOF

          cat > "$ZDOTDIR/.zshrc" <<-'EOF'
            # Isolated .zshrc for Voice2Action dev shell (oh-my-zsh + agnoster)
            export DISABLE_AUTO_UPDATE="true"
            ZSH_THEME="agnoster"
            plugins=(git)
            source "$ZSH/oh-my-zsh.sh"

            # Extra prompt context (append project tag after theme prompt rebuilds)
            precmd_functions+=(v2a_project_tag)
            v2a_project_tag() {
              # agnoster sets PROMPT; append subtle tag if not already present
              case "$PROMPT" in
                *voice2action*) ;;
                *) PROMPT="$PROMPT%F{cyan}[voice2action]%f " ;;
              esac
            }
          EOF

          # Relaunch into zsh (login shell) exactly once so user lands in zsh with isolated config.
          if [ -z "$ZSH_VERSION" ]; then
            exec ${pkgs.zsh}/bin/zsh -l
          fi
        '';
      };

      # Minimal build shell for CI / non-interactive builds.
      # Usage: nix develop .#build -c dotnet build
      # Does NOT attempt to fetch secrets (runtime-only). Keeps environment pure and deterministic.
      devShells.build = pkgs.mkShell {
        name = "voice2action-build";
        buildInputs = with pkgs; [
          dotnet-sdk_9
        ];
        DOTNET_CLI_TELEMETRY_OPTOUT = 1;
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1;
        DOTNET_NOLOGO = 1;
        DOTNET_MULTILEVEL_LOOKUP = 0;
        # Provide DOTNET_ROOT only; secrets intentionally omitted.
        shellHook = ''
          export DOTNET_ROOT=${pkgs.dotnet-sdk_9}/share/dotnet
          echo "[build shell] Secrets not loaded (expected)." >&2
        '';
      };
    });
}
