{
  description = "Ratatoskr - server-side Valheim world queries over RCON";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { nixpkgs, ... }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" "aarch64-darwin" "x86_64-darwin" ];
      forAll = f: nixpkgs.lib.genAttrs systems (s: f nixpkgs.legacyPackages.${s});
    in {
      packages = forAll (pkgs: rec {
        bot = pkgs.python3Packages.buildPythonApplication {
          pname = "ratatoskr-bot";
          version = "0.1.0";
          pyproject = true;
          src = ./bot;
          build-system = [ pkgs.python3Packages.setuptools ];
          dependencies = [ pkgs.python3Packages.discordpy ];
          pythonImportsCheck = [ "ratatoskr_bot" ];
          meta.mainProgram = "ratatoskr-bot";
        };
        default = bot;
      });

      devShells = forAll (pkgs: {
        default = pkgs.mkShell {
          packages = [ pkgs.dotnet-sdk_8 ];
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        };
      });
    };
}
