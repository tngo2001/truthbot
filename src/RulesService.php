<?php

namespace TruFraudBot;

/**
 * Read/write rules from rules.txt (one rule per line). Used as context for fb (rules) chat.
 */
final class RulesService
{
    private string $path;

    public function __construct(?string $path = null)
    {
        $this->path = $path ?? (getcwd() . DIRECTORY_SEPARATOR . 'rules.txt');
    }

    public function getRulesFilePath(): string
    {
        return $this->path;
    }

    public function read(): string
    {
        if (!is_file($this->path)) {
            return '';
        }
        $content = @file_get_contents($this->path) ?: '';
        return trim($content);
    }

    /** @return list<string> */
    public function getLines(): array
    {
        $content = $this->read();
        if (trim($content) === '') {
            return [];
        }
        $lines = explode("\n", $content);
        $result = [];
        foreach ($lines as $line) {
            $line = trim($line);
            if ($line !== '') {
                $result[] = $line;
            }
        }
        return $result;
    }

    public function add(string $rule): void
    {
        $rule = trim($rule);
        if ($rule === '') {
            return;
        }
        $dir = dirname($this->path);
        if ($dir !== '' && !is_dir($dir)) {
            @mkdir($dir, 0755, true);
        }
        file_put_contents($this->path, $rule . "\n", FILE_APPEND | LOCK_EX);
    }

    public function remove(int $oneBasedIndex): bool
    {
        $lines = $this->getLines();
        if ($oneBasedIndex < 1 || $oneBasedIndex > count($lines)) {
            return false;
        }
        array_splice($lines, $oneBasedIndex - 1, 1);
        $content = $lines === [] ? '' : implode("\n", $lines) . "\n";
        file_put_contents($this->path, $content, LOCK_EX);
        return true;
    }

    public function edit(int $oneBasedIndex, string $newText): bool
    {
        $newText = trim($newText);
        $lines = $this->getLines();
        if ($oneBasedIndex < 1 || $oneBasedIndex > count($lines) || $newText === '') {
            return false;
        }
        $lines[$oneBasedIndex - 1] = $newText;
        file_put_contents($this->path, implode("\n", $lines) . "\n", LOCK_EX);
        return true;
    }
}
