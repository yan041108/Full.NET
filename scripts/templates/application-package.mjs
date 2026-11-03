// 保留冻结依赖及 pnpm 策略，只公开实际随应用交付的命令，不能继承仓库专用测试/发布脚本。
export function createApplicationPackage(frameworkPackage, applicationPackage) {
  if (!applicationPackage?.scripts || typeof applicationPackage.scripts !== 'object'
    || Array.isArray(applicationPackage.scripts)
    || Object.keys(applicationPackage.scripts).length === 0
    || Object.values(applicationPackage.scripts).some(command => typeof command !== 'string' || !command.trim())) {
    throw new Error('Application template must declare nonempty commands');
  }
  return { ...frameworkPackage, name: applicationPackage.name, private: true,
    scripts: { ...applicationPackage.scripts } };
}
