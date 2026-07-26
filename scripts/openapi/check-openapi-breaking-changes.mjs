import path from 'node:path';
import {
  compareContractSets,
  loadContractsAtGitRef,
  loadContractsFromDirectory
} from './openapi-contract-compatibility.mjs';

function parseArguments(argumentsList) {
  const options = {};

  for (let index = 0; index < argumentsList.length; index += 2) {
    const optionName = argumentsList[index];
    const optionValue = argumentsList[index + 1];
    if (!optionName?.startsWith('--') || !optionValue) {
      throw new Error(
        'Usage: --base-ref <git-ref> [--repository-root <path>] or ' +
          '--baseline-directory <path> --current-directory <path>'
      );
    }

    const propertyName = optionName
      .slice(2)
      .replace(/-([a-z])/gu, (_, character) => character.toUpperCase());
    options[propertyName] = optionValue;
  }

  return options;
}

async function loadContractSets(options) {
  if (options.baselineDirectory && options.currentDirectory) {
    return {
      baselineContracts: await loadContractsFromDirectory(
        path.resolve(options.baselineDirectory)
      ),
      currentContracts: await loadContractsFromDirectory(
        path.resolve(options.currentDirectory)
      )
    };
  }

  if (options.baseRef) {
    const repositoryRoot = path.resolve(
      options.repositoryRoot ?? process.cwd()
    );
    return {
      baselineContracts: await loadContractsAtGitRef(
        repositoryRoot,
        options.baseRef
      ),
      currentContracts: await loadContractsFromDirectory(
        path.join(repositoryRoot, 'contracts/openapi')
      )
    };
  }

  throw new Error(
    'Usage: --base-ref <git-ref> [--repository-root <path>] or ' +
      '--baseline-directory <path> --current-directory <path>'
  );
}

async function main() {
  try {
    const options = parseArguments(process.argv.slice(2));
    const { baselineContracts, currentContracts } =
      await loadContractSets(options);
    const changes = compareContractSets(
      baselineContracts,
      currentContracts
    );

    if (changes.length > 0) {
      console.error(
        `OpenAPI backward compatibility check failed with ${changes.length} change(s):`
      );
      for (const change of changes) {
        console.error(`- ${change}`);
      }
      process.exitCode = 1;
      return;
    }

    console.log(
      'OpenAPI compatibility check passed: ' +
        `${baselineContracts.size} baseline contract(s), ` +
        `${currentContracts.size} current contract(s).`
    );
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 2;
  }
}

await main();
