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
          curl
          openssl
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
                (🚧\ *) ;;
                (*) PROMPT="🚧 $PROMPT" ;;
              esac
            }
          EOF

          # --- .NET Aspire CLI -------------------------------------------------
          # We install (or reuse cached) Aspire CLI into a project-local directory to avoid
          # mutating $HOME and to keep the dev shell self‑contained. This happens only for
          # interactive dev shell (not the minimal build shell) to keep CI reproducible.
          #
          # Override defaults with:
          #   ASPIRE_QUALITY=staging nix develop        (quality: dev|staging|release)
          #   ASPIRE_ARCH=arm64 nix develop             (architecture override)
          #   ASPIRE_FORCE_INSTALL=1 nix develop        (force re-install even if present)
          #   ASPIRE_VERSION=9.0.0-preview.x.y         (pin a specific version; clears quality)
          # Skip install entirely with:
          #   ASPIRE_SKIP_INSTALL=1 nix develop
          ASPIRE_INSTALL_DIR="$PWD/.aspire/bin"
          ASPIRE_CACHE_DIR="$PWD/.aspire/cache"
          ASPIRE_INSTALL_SCRIPT_URL="''${ASPIRE_INSTALL_SCRIPT_URL:-https://aka.ms/aspire/get/install.sh}"
          ASPIRE_INSTALL_SCRIPT_PATH="$ASPIRE_CACHE_DIR/get-aspire-cli.sh"
          ASPIRE_QUALITY="''${ASPIRE_QUALITY:-release}"
          ASPIRE_ARCH="''${ASPIRE_ARCH:-x64}"
          ASPIRE_VERSION="''${ASPIRE_VERSION:-}"  # if set, script ignores quality daily channel URLs
          ASPIRE_MAX_SCRIPT_AGE_SEC="''${ASPIRE_MAX_SCRIPT_AGE_SEC:-86400}" # 24h

          if [ "''${ASPIRE_SKIP_INSTALL:-0}" != "1" ]; then
            mkdir -p "$ASPIRE_CACHE_DIR" "$ASPIRE_INSTALL_DIR"
            fetch_script=0
            if [ ! -f "$ASPIRE_INSTALL_SCRIPT_PATH" ]; then
              fetch_script=1
            else
              # Re-fetch if older than threshold or forced refresh
              script_age=$(( $(date +%s) - $(stat -c %Y "$ASPIRE_INSTALL_SCRIPT_PATH" 2>/dev/null || echo 0) ))
              if [ "$script_age" -gt "$ASPIRE_MAX_SCRIPT_AGE_SEC" ]; then
                fetch_script=1
              fi
              if [ "''${ASPIRE_INSTALL_SCRIPT_REFRESH:-0}" = "1" ]; then
                fetch_script=1
              fi
            fi
            if [ $fetch_script -eq 1 ]; then
              echo "[dev shell] Fetching Aspire installer script from $ASPIRE_INSTALL_SCRIPT_URL" >&2
              if ! curl -fsSL "$ASPIRE_INSTALL_SCRIPT_URL" -o "$ASPIRE_INSTALL_SCRIPT_PATH"; then
                echo "[warn] Failed to download Aspire install script; skipping CLI install" >&2
              else
                chmod +x "$ASPIRE_INSTALL_SCRIPT_PATH"
              fi
            fi

            need_install=0
            if [ "''${ASPIRE_FORCE_INSTALL:-0}" = "1" ]; then
              need_install=1
            elif [ ! -x "$ASPIRE_INSTALL_DIR/aspire" ]; then
              need_install=1
            fi
              if [ $need_install -eq 1 ] && [ -x "$ASPIRE_INSTALL_SCRIPT_PATH" ]; then
                echo "[dev shell] Installing .NET Aspire CLI (quality=$ASPIRE_QUALITY version=''${ASPIRE_VERSION:-latest} arch=$ASPIRE_ARCH) ..." >&2
                # Use an isolated fake HOME so installer does not touch the real user's profile and
                # always finds a shell config file (prevents 'config_file: unbound variable').
                ASPIRE_FAKE_HOME="$PWD/.aspire/home"
                mkdir -p "$ASPIRE_FAKE_HOME"
                # Provide stub bash and zsh rc files so installer path logic succeeds regardless of detected shell.
                [ -f "$ASPIRE_FAKE_HOME/.bashrc" ] || echo '# stub for Aspire CLI installer' > "$ASPIRE_FAKE_HOME/.bashrc"
                [ -f "$ASPIRE_FAKE_HOME/.bash_profile" ] || echo '# stub profile' > "$ASPIRE_FAKE_HOME/.bash_profile"
                [ -f "$ASPIRE_FAKE_HOME/.profile" ] || echo '# stub profile' > "$ASPIRE_FAKE_HOME/.profile"
                [ -f "$ASPIRE_FAKE_HOME/.zshrc" ] || echo '# stub zshrc' > "$ASPIRE_FAKE_HOME/.zshrc"
                if [ -n "$ASPIRE_VERSION" ]; then
                  HOME="$ASPIRE_FAKE_HOME" bash "$ASPIRE_INSTALL_SCRIPT_PATH" --install-path "$ASPIRE_INSTALL_DIR" --version "$ASPIRE_VERSION" --os linux --arch "$ASPIRE_ARCH" || echo "[warn] Aspire CLI install failed (non-fatal)" >&2
                else
                  HOME="$ASPIRE_FAKE_HOME" bash "$ASPIRE_INSTALL_SCRIPT_PATH" --install-path "$ASPIRE_INSTALL_DIR" --quality "$ASPIRE_QUALITY" --os linux --arch "$ASPIRE_ARCH" || echo "[warn] Aspire CLI install failed (non-fatal)" >&2
                fi
              fi
            if [ -x "$ASPIRE_INSTALL_DIR/aspire" ]; then
              export PATH="$ASPIRE_INSTALL_DIR:$PATH"
            fi
          fi
          # ---------------------------------------------------------------------

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
        buildInputs = with pkgs; [ dotnet-sdk_9 ];
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
