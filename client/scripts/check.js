import { readdirSync } from 'node:fs';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
function check(directory) {
  for (const file of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, file.name);
    if (file.isDirectory()) check(path);
    else if (file.name.endsWith('.js')) {
      const result = spawnSync(process.execPath, ['--check', path], { stdio: 'inherit' });
      if (result.status) process.exit(result.status);
    }
  }
}
check('src'); check('tests'); console.log('JavaScript syntax: OK');
