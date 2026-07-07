// SPDX-License-Identifier: MIT
// Thin, blittable C ABI over Box3D for Unity P/Invoke.
// Avoids marshalling Box3D's internal *Def structs across the managed boundary,
// and does the batched transform read-back on the native side (the b3BodyEvents
// contiguous move-array), mirroring Unity Physics Core 2D's bodyUpdateEvents design.

#include "box3d/box3d.h"
#include <stdint.h>

// Pose record written back to the managed side, indexed by body slot.
// Layout must match Box3DNative.Pose in C# exactly (7 contiguous floats).
typedef struct b3uPose
{
	float px, py, pz;
	float qx, qy, qz, qw;
} b3uPose;

// External-scheduler world: Box3D creates NO threads and drives its parallel tasks
// through the supplied enqueue/finish callbacks (Unity's C# Job System on the managed
// side). Total thread count stays at Unity's existing worker pool (~core count).
BOX3D_EXPORT uint32_t b3u_CreateWorldExternal( float gx, float gy, float gz, uint32_t workerCount,
											   void* enqueueTask, void* finishTask, void* userContext )
{
	b3WorldDef def = b3DefaultWorldDef();
	def.gravity = ( b3Vec3 ){ gx, gy, gz };
	def.workerCount = workerCount;
	def.enqueueTask = (b3EnqueueTaskCallback*)enqueueTask;
	def.finishTask = (b3FinishTaskCallback*)finishTask;
	def.userTaskContext = userContext;
	return b3StoreWorldId( b3CreateWorld( &def ) );
}

BOX3D_EXPORT void b3u_DestroyWorld( uint32_t world )
{
	b3DestroyWorld( b3LoadWorldId( world ) );
}

BOX3D_EXPORT void b3u_Step( uint32_t world, float dt, int subStepCount )
{
	b3World_Step( b3LoadWorldId( world ), dt, subStepCount );
}

static b3BodyId b3u_MakeBody( uint32_t world, int type, float px, float py, float pz, float qx, float qy, float qz,
							  float qw, int32_t slot )
{
	b3BodyDef bd = b3DefaultBodyDef();
	bd.type = (b3BodyType)type;
	bd.position = ( b3Pos ){ px, py, pz };
	bd.rotation = ( b3Quat ){ { qx, qy, qz }, qw };
	bd.userData = (void*)(intptr_t)slot;
	return b3CreateBody( b3LoadWorldId( world ), &bd );
}

BOX3D_EXPORT uint64_t b3u_CreateBox( uint32_t world, int type, float px, float py, float pz, float qx, float qy, float qz,
									 float qw, float hx, float hy, float hz, float density, float friction,
									 float restitution, int32_t slot )
{
	b3BodyId id = b3u_MakeBody( world, type, px, py, pz, qx, qy, qz, qw, slot );
	b3BoxHull hull = b3MakeBoxHull( hx, hy, hz );
	b3ShapeDef sd = b3DefaultShapeDef();
	sd.density = density;
	sd.baseMaterial.friction = friction;
	sd.baseMaterial.restitution = restitution;
	b3CreateHullShape( id, &sd, &hull.base );
	return b3StoreBodyId( id );
}

BOX3D_EXPORT uint64_t b3u_CreateSphere( uint32_t world, int type, float px, float py, float pz, float qx, float qy,
										float qz, float qw, float radius, float density, float friction,
										float restitution, int32_t slot )
{
	b3BodyId id = b3u_MakeBody( world, type, px, py, pz, qx, qy, qz, qw, slot );
	b3Sphere sphere = { { 0.0f, 0.0f, 0.0f }, radius };
	b3ShapeDef sd = b3DefaultShapeDef();
	sd.density = density;
	sd.baseMaterial.friction = friction;
	sd.baseMaterial.restitution = restitution;
	b3CreateSphereShape( id, &sd, &sphere );
	return b3StoreBodyId( id );
}

// Batched read-back. Writes the pose of every body that moved this step into
// out[slot], where slot is the userData index supplied at creation.
// Returns the number of move events (bodies that moved). This is the contiguous,
// only-what-moved array we hand to an IJobParallelForTransform on the C# side.
BOX3D_EXPORT int b3u_WritePoses( uint32_t world, b3uPose* out, int capacity )
{
	b3BodyEvents ev = b3World_GetBodyEvents( b3LoadWorldId( world ) );
	for ( int i = 0; i < ev.moveCount; ++i )
	{
		const b3BodyMoveEvent* m = &ev.moveEvents[i];
		int slot = (int)(intptr_t)m->userData;
		if ( slot < 0 || slot >= capacity )
			continue;
		b3Transform t = m->transform; // single precision: b3WorldTransform == b3Transform
		out[slot].px = t.p.x;
		out[slot].py = t.p.y;
		out[slot].pz = t.p.z;
		out[slot].qx = t.q.v.x;
		out[slot].qy = t.q.v.y;
		out[slot].qz = t.q.v.z;
		out[slot].qw = t.q.s;
	}
	return ev.moveCount;
}
