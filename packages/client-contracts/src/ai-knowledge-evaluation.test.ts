import {it,expect} from 'vitest';
import type {AiKnowledgeEvaluationWorkspace,AiKnowledgeEvaluationMutation} from './generated/models.generated.js';
import {validateKnowledgeEvaluation,validateKnowledgeEvaluationMutation,validateKnowledgeEvaluationPage} from './ai-knowledge-evaluation.js';
import {aiCreateKnowledgeEvaluationSet,aiPrepareKnowledgeEvaluationFeedback} from './generated/operations.generated.js';
import type {HttpClient} from './http.js';
const id=(n:number)=>`01900000-0000-7000-8000-${String(n).padStart(12,'0')}`;
type Mutable<T>={-readonly[K in keyof T]:T[K]};
type Workspace=Omit<Mutable<AiKnowledgeEvaluationWorkspace>,'revision'>&{revision:Mutable<AiKnowledgeEvaluationWorkspace['revision']>};
const workspace=():Workspace=>({id:id(1),knowledgeBaseId:id(2),version:1,approvalState:'none',approvalId:null,canIndependentlyApprove:false,
  revision:{id:id(3),number:1,name:'人工测试集',state:'draft',contentHash:'a'.repeat(64),sourceHash:'b'.repeat(64),sourceCount:0,createdAtUtc:'2026-10-30T00:00:00Z',restoredFromRevisionId:null,
    cases:[{id:id(4),sourceKey:null,independentBasis:'独立人工编写',sample:{languageTag:'zh-CN',question:'脱敏问题',expectedAnswer:'人工答案',evidence:'人工依据',shouldRefuse:false}}]}});
it('retains restrictions after every derived case has been removed',()=>{const value=workspace();value.revision.sourceCount=2;expect(validateKnowledgeEvaluation(value,id(2))).toBe(value);});
it('requires the exact knowledge base set and historical revision',()=>{const variations:readonly (readonly [string,string?,string?])[]=[[id(9)],[id(2),id(9)],[id(2),id(1),id(9)]];for(const keys of variations)expect(()=>validateKnowledgeEvaluation(workspace(),keys[0],keys[1],keys[2])).toThrow();});
it.each(['published','running','approved'])('rejects %s as a dataset revision state',state=>{const value=workspace();value.revision.state=state;expect(()=>validateKnowledgeEvaluation(value,id(2))).toThrow();});
it('rejects approval of draft or inconsistent approval identities',()=>{const value=workspace();value.approvalState='approved';value.approvalId=id(5);expect(()=>validateKnowledgeEvaluation(value,id(2))).toThrow();value.revision.state='frozen';expect(validateKnowledgeEvaluation(value,id(2))).toBe(value);value.approvalId=null;expect(()=>validateKnowledgeEvaluation(value,id(2))).toThrow();});
it('rejects duplicate cases and source downgrade',()=>{const value=workspace();value.revision.cases=[value.revision.cases[0]!,value.revision.cases[0]!];expect(()=>validateKnowledgeEvaluation(value,id(2))).toThrow();value.revision.cases=[{...value.revision.cases[0]!,sourceKey:'c'.repeat(64),independentBasis:null}];expect(()=>validateKnowledgeEvaluation(value,id(2))).toThrow();value.revision.sourceCount=1;expect(validateKnowledgeEvaluation(value,id(2))).toBe(value);});
it('does not promote editor independence on an unfrozen revision',()=>{const value=workspace();value.canIndependentlyApprove=true;expect(()=>validateKnowledgeEvaluation(value,id(2))).toThrow();});
it('keeps a committed source-unavailable receipt without content',()=>{const receipt:AiKnowledgeEvaluationMutation={workspace:null,savedRevisionId:id(3),replayed:false,deliveryState:'source_unavailable'};expect(validateKnowledgeEvaluationMutation(receipt,id(2))).toBe(receipt);expect(()=>validateKnowledgeEvaluationMutation({...receipt,workspace:workspace()},id(2))).toThrow();});
it('replay can refer to an earlier saved revision without changing its identity',()=>{const receipt={workspace:workspace(),savedRevisionId:id(7),replayed:true,deliveryState:'available'};expect(validateKnowledgeEvaluationMutation(receipt,id(2)).savedRevisionId).toBe(id(7));});
it('rejects duplicate sets and unbounded result windows',()=>{const value=workspace();expect(()=>validateKnowledgeEvaluationPage({items:[value,value],nextAfterId:null},id(2))).toThrow();expect(()=>validateKnowledgeEvaluationPage({items:Array.from({length:9},(_,i)=>({...value,id:id(20+i)})),nextAfterId:null},id(2))).toThrow();});
it('uses generated transport for explicit target preparation and immutable creation',async()=>{
 const signal=new AbortController().signal;const paths:string[]=[];
 const http={request:async(path:string,init:RequestInit,actual?:AbortSignal)=>{paths.push(path);expect(init.method).toBe('POST');expect(actual).toBe(signal);return path.endsWith('feedback-candidates')?{persistenceToken:'protected',sourceHash:'a'.repeat(64),sampleHash:'b'.repeat(64),expiresAtUtc:'2026-10-30T00:10:00Z'}:{workspace:workspace(),savedRevisionId:id(3),replayed:false,deliveryState:'available'};}} as HttpClient;
 await aiPrepareKnowledgeEvaluationFeedback(http,{knowledgeBaseId:id(2),body:{candidateToken:'old-purpose-token'}},signal);
 await aiCreateKnowledgeEvaluationSet(http,{knowledgeBaseId:id(2),body:{requestKey:id(8),expectedRevisionId:null,expectedVersion:0,name:'人工测试集',cases:[{id:null,sample:workspace().revision.cases[0]!.sample,independentBasis:'人工编写',feedbackCandidateToken:null}],confirmManualRedaction:true,freeze:false}},signal);
 expect(paths).toEqual([`/api/v1/ai/knowledge-bases/${id(2)}/evaluation-sets/feedback-candidates`,`/api/v1/ai/knowledge-bases/${id(2)}/evaluation-sets`]);
});
