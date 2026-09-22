using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace NetworkOptimizer.Updater;

public static class Program
{
    public static int Main(string[] args)
    {
        string? targetPath = null;
        string? packagePath = null;
        int targetPid = -1;
        bool restart = false;

        // 1. Parseo de argumentos de línea de comandos
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if ((arg.Equals("--target", StringComparison.OrdinalIgnoreCase) || arg.Equals("-t", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                targetPath = args[++i];
            }
            else if ((arg.Equals("--package", StringComparison.OrdinalIgnoreCase) || arg.Equals("-p", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                packagePath = args[++i];
            }
            else if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out targetPid);
            }
            else if (arg.Equals("--restart", StringComparison.OrdinalIgnoreCase) || arg.Equals("-r", StringComparison.OrdinalIgnoreCase))
            {
                restart = true;
            }
        }

        if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(packagePath))
        {
            return 1;
        }

        if (!File.Exists(packagePath))
        {
            return 2;
        }

        // 2. Esperar a que el proceso principal de NetworkOptimizer termine
        if (targetPid > 0)
        {
            try
            {
                var process = Process.GetProcessById(targetPid);
                if (!process.HasExited)
                {
                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch { }
                    }
                }
            }
            catch (ArgumentException)
            {
                // El proceso ya no existe
            }
            catch
            {
                // Ignorar excepciones al esperar
            }
        }

        // Breve pausa para garantizar liberación de descriptores de archivos en Windows
        Thread.Sleep(800);

        var backupPath = targetPath + ".bak";
        bool backupCreated = false;

        // 3. Crear copia de respaldo de la versión anterior
        try
        {
            if (File.Exists(targetPath))
            {
                File.Copy(targetPath, backupPath, overwrite: true);
                backupCreated = true;
            }
        }
        catch
        {
            // Continuar incluso si el backup falla por permisos no críticos
        }

        // 4. Reemplazo seguro con reintentos
        bool replaceSuccess = false;
        for (int attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                File.Copy(packagePath, targetPath, overwrite: true);
                replaceSuccess = true;
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(1000);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(1000);
            }
        }

        // 5. Gestión post-reemplazo
        if (replaceSuccess)
        {
            // Éxito: Limpiar paquete temporal y backup
            TryDelete(packagePath);
            if (backupCreated)
            {
                TryDelete(backupPath);
            }

            // Iniciar nueva versión si se solicitó
            if (restart && File.Exists(targetPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = targetPath,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignorar fallos al relanzar
                }
            }

            return 0;
        }
        else
        {
            // Fallo: Restaurar backup anterior
            if (backupCreated && File.Exists(backupPath))
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        File.Copy(backupPath, targetPath, overwrite: true);
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(1000);
                    }
                }
            }

            TryDelete(packagePath);
            return 3;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignorar errores en limpieza de temporales
        }
    }
}
