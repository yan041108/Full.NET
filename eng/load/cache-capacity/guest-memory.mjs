import {randomUUID} from 'node:crypto';
import {guestMemory} from './resource-budget.mjs';
/** 未知资源结果必须保留外层租约；记录失败也不能让异常展开并触发释放。 */
export function preserveUnknownBoundary(error,record){try{record(error);}finally{process.exit(2);}}
/** 从实际 Docker 引擎读取内存；正式阶段复用自有 SDK，启动前使用可核对的极小只读探针。 */
export async function readEngineGuestMemory({docker,imageId,owner,container,journal}){
 if(!/^sha256:[0-9a-f]{64}$/u.test(imageId)||!/^fullnet-cache-capacity-[a-z0-9-]+$/u.test(owner))throw Error('Exact image and capacity owner required');
 if(container){
  const [actual]=JSON.parse(await docker(['inspect',container]));
  if(actual.Config?.Labels?.['fullnet.cache-capacity.owner']!==owner||actual.Image!==imageId||!actual.State.Running
   ||actual.HostConfig.Memory!==8*1024**3||actual.HostConfig.MemorySwap!==8*1024**3||actual.HostConfig.NanoCpus!==4e9)throw Error('Actual SDK owner or limits differ');
  return guestMemory(await docker(['exec',actual.Id,'cat','/proc/meminfo']));
 }
 const nonce=randomUUID(),label='fullnet.cache-memory-probe='+nonce,memory=32*1024**2;let id,intent=false,createConfirmed=false;
 const owned=a=>a.Id===id&&a.Config?.Labels?.['fullnet.cache-memory-probe']===nonce&&a.Config?.Labels?.['fullnet.cache-capacity.owner']===owner&&a.Image===imageId;
 try{
  await journal({stage:'memory-probe-intent',nonce,imageId});intent=true;
  const created=await docker(['create','--pull=never','--name',owner+'-memory-'+nonce,'--label',label,'--label','fullnet.cache-capacity.owner='+owner,
   '--memory',String(memory),'--memory-swap',String(memory),'--cpus','0.1','--pids-limit','16','--network','none','--read-only','--cap-drop','ALL','--security-opt','no-new-privileges','--user','65534:65534','--entrypoint','/bin/cat',imageId,'/proc/meminfo']);
  createConfirmed=true;id=created.trim();
  if(!/^[0-9a-f]{64}$/u.test(id))throw Error('Exact memory probe ID missing');
  const [actual]=JSON.parse(await docker(['inspect',id]));
  if(!owned(actual)||actual.State.Status!=='created'||actual.HostConfig.Memory!==memory||actual.HostConfig.MemorySwap!==memory||actual.HostConfig.NanoCpus!==1e8||actual.HostConfig.PidsLimit!==16||actual.HostConfig.NetworkMode!=='none'||!actual.HostConfig.ReadonlyRootfs)throw Error('Actual memory probe owner or quota differs');
  await journal({stage:'memory-probe-budget-verified',nonce,id,memory});
  const raw=await docker(['start','-a',id]);const [closed]=JSON.parse(await docker(['inspect',id]));
  if(!owned(closed)||closed.State.Running||closed.State.ExitCode!==0||closed.State.OOMKilled)throw Error('Memory probe did not exit normally');
  return guestMemory(raw);
 }finally{
  if(intent)try{
   if(!createConfirmed)throw Error('Memory probe create daemon completion is unknown; an empty listing cannot confirm return');
   if(!id){const ids=(await docker(['ps','-aq','--no-trunc','--filter','label='+label])).trim().split(/\s+/u).filter(Boolean);if(ids.length>1)throw Error('Memory probe create outcome ambiguous');id=ids[0];}
   if(id){const [actual]=JSON.parse(await docker(['inspect',id]));if(!owned(actual))throw Error('Memory probe cleanup owner differs');await docker(['rm','-f','-v',id]);}
   if((await docker(['ps','-aq','--no-trunc','--filter','label='+label])).trim())throw Error('Memory probe still present');
   await journal({stage:'memory-probe-returned',nonce,id});
  }catch(error){error.retainResources=true;throw error;}
 }
}
