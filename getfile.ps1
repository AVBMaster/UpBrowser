# 递归遍历目录，排除指定文件夹和文件
function Get-AllFilesRecursively {
    param(
        [string]$Path = ".",
        [string[]]$ExcludeDirs = @("bin", "obj", ".git", ".vs"),
        [string[]]$ExcludeFiles = @(".gitattributes", ".gitignore", "license.txt","getfile.ps1","path.txt","file.txt")
    )
    
    # 获取当前目录下的所有项目（文件和目录），排除指定目录
    $items = Get-ChildItem -Path $Path -Force | Where-Object { 
        $ExcludeDirs -notcontains $_.Name 
    }
    
    foreach ($item in $items) {
        if ($item.PSIsContainer) {
            # 如果是目录，递归处理
            Get-AllFilesRecursively -Path $item.FullName -ExcludeDirs $ExcludeDirs -ExcludeFiles $ExcludeFiles
        } else {
            # 排除指定文件
            if ($ExcludeFiles -contains $item.Name) {
                continue
            }
            
            # 如果是文件，输出文件名和内容
            try {
                # 尝试以 UTF-8 读取
                $fileContent = Get-Content -Path $item.FullName -Raw -Encoding UTF8 -ErrorAction Stop
            } catch {
                try {
                    # 如果 UTF-8 失败，尝试默认编码
                    $fileContent = Get-Content -Path $item.FullName -Raw -ErrorAction Stop
                } catch {
                    $fileContent = "[无法读取文件内容: $_]"
                }
            }
            
            # 输出相对路径和内容
            $relativePath = $item.FullName.Substring((Get-Location).Path.Length + 1)
            
            # 输出分隔线和文件信息
            Write-Output "========================================"
            Write-Output "文件: $relativePath"
            Write-Output "========================================"
            Write-Output $fileContent
            Write-Output ""  # 空行分隔
            Write-Output ""
        }
    }
}

# 执行函数
Get-AllFilesRecursively